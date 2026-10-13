using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class RefreshTests : ApiTests
{
    public RefreshTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Sign_up_sets_the_refresh_cookie_and_stores_only_its_hash()
    {
        using var response = await PostOverHttpsAsync("/api/v1/auth/sign-up", SignUpBody(NewEmail()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var token = RefreshCookieAssert.Set(response, Session.AbsoluteLifetime);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var stored = await FromDatabaseAsync(database => database.RefreshTokens.SingleAsync());
        Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(token)), stored.TokenHash);
        Assert.Null(stored.RetiredAt);
        Assert.Equal(SessionOf((await response.ReadEnvelope<SignedUpDto>()).Data!.AccessToken), stored.SessionId);
    }

    [Fact]
    public async Task Sign_in_sets_the_refresh_cookie()
    {
        using var response = await PostOverHttpsAsync(
            "/api/v1/auth/sign-in", new { email = ActorUser.User.Email, password = TestUser.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = RefreshCookieAssert.Set(response, Session.AbsoluteLifetime);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refresh_rotates_the_token_and_keeps_the_session()
    {
        var (signedUp, first) = await SignUpOverHttpsAsync();
        Time.Advance(TimeSpan.FromHours(1));

        using var response = await RefreshAsync(first);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = RefreshCookieAssert.Set(response, Session.AbsoluteLifetime - TimeSpan.FromHours(1));
        Assert.NotEqual(first, second);
        var refreshed = (await response.ReadEnvelope<SignedInDto>()).Data!;
        var sid = SessionOf(signedUp.AccessToken);
        Assert.Equal(sid, SessionOf(refreshed.AccessToken));
        Assert.Equal(signedUp.SessionExpiresAt, refreshed.SessionExpiresAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(Time.GetUtcNow().UtcDateTime.AddMinutes(15), refreshed.AccessTokenExpiresAt, TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(second, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var session = await FromDatabaseAsync(database => database.Sessions.SingleAsync(s => s.Id == sid));
        Assert.Equal(Time.GetUtcNow().UtcDateTime, session.LastUsedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(signedUp.SessionExpiresAt, session.ExpiresAt, TimeSpan.FromMilliseconds(1));
        var tokens = await FromDatabaseAsync(database => database.RefreshTokens.OrderBy(t => t.CreatedAt).ToListAsync());
        Assert.Equal(2, tokens.Count);
        Assert.Equal(Time.GetUtcNow().UtcDateTime, tokens[0].RetiredAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Null(tokens[1].RetiredAt);

        using var write = await SendWithTokenAsync(
            HttpMethod.Post, "/api/v1/workspaces", refreshed.AccessToken, new { name = "Design", visibility = "Public" });
        Assert.Equal(HttpStatusCode.Created, write.StatusCode);

        var logged = Assert.Single(Logs(), log => log.Id.Name == "SessionRefreshed");
        Assert.Equal(AuthLog.Category, logged.Category);
        Assert.Equal(LogLevel.Information, logged.Level);
        Assert.Equal(signedUp.User.Id.ToString(), logged.GetStructuredStateValue("sub"));
        Assert.Equal(sid.ToString(), logged.GetStructuredStateValue("sid"));
        Assert.Equal(tokens[1].Id.ToString(), logged.GetStructuredStateValue("refreshTokenId"));
    }

    [Fact]
    public async Task The_new_token_refreshes_again()
    {
        var (_, first) = await SignUpOverHttpsAsync();
        using var once = await RefreshAsync(first);
        var second = RefreshCookieAssert.Set(once, Session.AbsoluteLifetime);

        using var twice = await RefreshAsync(second);

        Assert.Equal(HttpStatusCode.OK, twice.StatusCode);
        Assert.NotEqual(second, RefreshCookieAssert.Set(twice, Session.AbsoluteLifetime));
    }

    [Fact]
    public async Task A_missing_cookie_gets_401_and_nothing_to_clear()
    {
        using var response = await RefreshAsync(cookie: null);

        await AssertNotAuthenticatedAsync(response);
        RefreshCookieAssert.Untouched(response);
        var logged = Assert.Single(Logs(), log => log.Id.Name == "RefreshRejected");
        Assert.Equal("missingCookie", logged.GetStructuredStateValue("reason"));
    }

    [Fact]
    public async Task An_unknown_token_gets_401_and_a_cleared_cookie()
    {
        using var response = await RefreshAsync("lnw_rt_" + new string('A', 43));

        await AssertNotAuthenticatedAsync(response);
        RefreshCookieAssert.Cleared(response);
        var logged = Assert.Single(Logs(), log => log.Id.Name == "RefreshRejected");
        Assert.Equal("unknownToken", logged.GetStructuredStateValue("reason"));
        Assert.Null(logged.GetStructuredStateValue("sid"));
    }

    [Fact]
    public async Task A_retired_token_inside_the_grace_window_gets_409_and_issues_nothing()
    {
        var (signedUp, first) = await SignUpOverHttpsAsync();
        using var winner = await RefreshAsync(first);
        var second = RefreshCookieAssert.Set(winner, Session.AbsoluteLifetime);
        Time.Advance(TimeSpan.FromSeconds(9));

        using var loser = await RefreshAsync(first);

        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        RefreshCookieAssert.Untouched(loser);
        var envelope = await loser.ReadEnvelope<object>();
        Assert.Null(envelope.Data);
        Assert.Equal(ErrorCodes.RefreshSuperseded, Assert.Single(envelope.Errors!).Code);
        var logged = Assert.Single(Logs(), log => log.Id.Name == "RefreshSuperseded");
        Assert.Equal(signedUp.User.Id.ToString(), logged.GetStructuredStateValue("sub"));
        Assert.Equal(SessionOf(signedUp.AccessToken).ToString(), logged.GetStructuredStateValue("sid"));

        using var retry = await RefreshAsync(second);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task A_retired_token_after_the_grace_window_ends_the_session()
    {
        var (signedUp, first) = await SignUpOverHttpsAsync();
        using var winner = await RefreshAsync(first);
        var second = RefreshCookieAssert.Set(winner, Session.AbsoluteLifetime);
        var refreshed = (await winner.ReadEnvelope<SignedInDto>()).Data!;
        Time.Advance(TimeSpan.FromSeconds(10));

        using var reused = await RefreshAsync(first);

        await AssertNotAuthenticatedAsync(reused);
        RefreshCookieAssert.Cleared(reused);
        var sid = SessionOf(signedUp.AccessToken);
        var session = await FromDatabaseAsync(database => database.Sessions.SingleAsync(s => s.Id == sid));
        Assert.Equal(SessionEndReason.TokenReused, session.EndReason);
        Assert.Equal(Time.GetUtcNow().UtcDateTime, session.EndedAt!.Value, TimeSpan.FromMilliseconds(1));

        using var live = await RefreshAsync(second);
        await AssertNotAuthenticatedAsync(live);
        using var write = await SendWithTokenAsync(
            HttpMethod.Post, "/api/v1/workspaces", refreshed.AccessToken, new { name = "Design", visibility = "Public" });
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);

        var reuse = Assert.Single(Logs(), log => log.Id.Name == "RefreshTokenReused");
        Assert.Equal(LogLevel.Warning, reuse.Level);
        Assert.Equal(sid.ToString(), reuse.GetStructuredStateValue("sid"));
        Assert.Equal(signedUp.User.Id.ToString(), reuse.GetStructuredStateValue("sub"));
        var reusedId = await FromDatabaseAsync(database => database.RefreshTokens
            .Where(t => t.TokenHash == SHA256.HashData(Encoding.ASCII.GetBytes(first)))
            .Select(t => t.Id)
            .SingleAsync());
        Assert.Equal(reusedId.ToString(), reuse.GetStructuredStateValue("refreshTokenId"));
        var ended = Assert.Single(Logs(), log => log.Id.Name == "SessionEnded");
        Assert.Equal(sid.ToString(), ended.GetStructuredStateValue("sid"));
        Assert.Equal("TokenReused", ended.GetStructuredStateValue("endReason"));
    }

    [Fact]
    public async Task Two_refreshes_at_once_have_exactly_one_winner()
    {
        var (_, first) = await SignUpOverHttpsAsync();

        var responses = await Task.WhenAll(RefreshAsync(first), RefreshAsync(first));

        try
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).Order());
            Assert.Equal(2, await FromDatabaseAsync(database => database.RefreshTokens.CountAsync()));
        }
        finally
        {
            Array.ForEach(responses, response => response.Dispose());
        }
    }

    [Fact]
    public async Task A_refresh_that_waits_on_the_winners_update_gets_409()
    {
        var (_, first) = await SignUpOverHttpsAsync();
        await using var winner = Services.CreateAsyncScope();
        var database = winner.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(first));
        await database.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .ExecuteUpdateAsync(t => t.SetProperty(r => r.RetiredAt, Time.GetUtcNow().UtcDateTime));

        var loser = RefreshAsync(first);
        await WaitForLockWaitAsync();
        await transaction.CommitAsync();
        using var response = await loser;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        RefreshCookieAssert.Untouched(response);
        Assert.Equal(1, await FromDatabaseAsync(db => db.RefreshTokens.CountAsync()));
    }

    [Fact]
    public async Task A_refresh_that_waits_on_a_session_ending_gets_401()
    {
        var (signedUp, first) = await SignUpOverHttpsAsync();
        var sid = SessionOf(signedUp.AccessToken);
        await using var ending = Services.CreateAsyncScope();
        var database = ending.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Sessions
            .Where(s => s.Id == sid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EndedAt, Time.GetUtcNow().UtcDateTime)
                .SetProperty(x => x.EndReason, SessionEndReason.SignedOut));

        var refresh = RefreshAsync(first);
        await WaitForLockWaitAsync();
        await transaction.CommitAsync();
        using var response = await refresh;

        await AssertNotAuthenticatedAsync(response);
        RefreshCookieAssert.Cleared(response);
        Assert.Equal(1, await FromDatabaseAsync(db => db.RefreshTokens.CountAsync()));
    }

    [Fact]
    public async Task A_session_idle_for_7_days_is_over()
    {
        var (_, first) = await SignUpOverHttpsAsync();
        Time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1));
        using var stillLive = await RefreshAsync(first);
        var second = RefreshCookieAssert.Set(stillLive, Session.AbsoluteLifetime - TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        Time.Advance(TimeSpan.FromDays(7));

        using var idle = await RefreshAsync(second);

        await AssertNotAuthenticatedAsync(idle);
        RefreshCookieAssert.Cleared(idle);
        var logged = Assert.Single(Logs(), log => log.Id.Name == "RefreshRejected");
        Assert.Equal("sessionEnded", logged.GetStructuredStateValue("reason"));
        Assert.NotNull(logged.GetStructuredStateValue("sid"));
    }

    [Fact]
    public async Task Refreshing_never_moves_the_30_day_limit()
    {
        var (_, token) = await SignUpOverHttpsAsync();
        for (var day = 6; day < 30; day += 6)
        {
            Time.Advance(TimeSpan.FromDays(6));
            using var refreshed = await RefreshAsync(token);
            token = RefreshCookieAssert.Set(refreshed, Session.AbsoluteLifetime - TimeSpan.FromDays(day));
        }

        Time.Advance(TimeSpan.FromDays(6));
        using var expired = await RefreshAsync(token);

        await AssertNotAuthenticatedAsync(expired);
        RefreshCookieAssert.Cleared(expired);
    }

    [Fact]
    public async Task An_ended_session_refreshes_no_more()
    {
        var (signedUp, token) = await SignUpOverHttpsAsync();
        var sid = SessionOf(signedUp.AccessToken);
        await FromDatabaseAsync(database => database.Sessions
            .Where(s => s.Id == sid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EndedAt, Time.GetUtcNow().UtcDateTime)
                .SetProperty(x => x.EndReason, SessionEndReason.SignedOut)));

        using var response = await RefreshAsync(token);

        await AssertNotAuthenticatedAsync(response);
        RefreshCookieAssert.Cleared(response);
    }

    [Fact]
    public async Task A_deleted_user_refreshes_no_more()
    {
        var (signedUp, token) = await SignUpOverHttpsAsync();
        await FromDatabaseAsync(database => database.Users
            .Where(u => u.Id == signedUp.User.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.DeletedAt, Time.GetUtcNow().UtcDateTime)));

        using var response = await RefreshAsync(token);

        await AssertNotAuthenticatedAsync(response);
        RefreshCookieAssert.Cleared(response);
    }

    [Fact]
    public async Task The_refresh_counter_counts_each_result()
    {
        using var results = new MetricCollector<long>(
            Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            AuthMeter.Name,
            AuthMeter.RefreshName);
        var (_, first) = await SignUpOverHttpsAsync();

        (await RefreshAsync(first)).Dispose();
        (await RefreshAsync(first)).Dispose();
        Time.Advance(TimeSpan.FromSeconds(10));
        (await RefreshAsync(first)).Dispose();
        (await RefreshAsync(cookie: null)).Dispose();

        Assert.Equal(
            ["rotated", "superseded", "reused", "rejected"],
            results.GetMeasurementSnapshot().Select(measurement => measurement.Tags["result"]));
    }

    [Fact]
    public async Task Refresh_events_never_carry_the_token_or_its_hash()
    {
        var (_, first) = await SignUpOverHttpsAsync();
        using var once = await RefreshAsync(first);
        var second = RefreshCookieAssert.Set(once, Session.AbsoluteLifetime);
        Time.Advance(TimeSpan.FromSeconds(10));
        (await RefreshAsync(first)).Dispose();

        var secrets = new[] { first, second }
            .SelectMany(token => new[] { token, Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token))) })
            .ToList();
        Assert.DoesNotContain(Logs(), log => secrets.Any(secret =>
            log.Message.Contains(secret, StringComparison.OrdinalIgnoreCase)
            || (log.StructuredState ?? []).Any(pair => pair.Value?.Contains(secret, StringComparison.OrdinalIgnoreCase) == true)));
    }

    private static async Task AssertNotAuthenticatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var envelope = await response.ReadEnvelope<object>();
        Assert.Equal("Sign in again.", envelope.Message);
        Assert.Equal(ErrorCodes.NotAuthenticated, Assert.Single(envelope.Errors!).Code);
    }
}
