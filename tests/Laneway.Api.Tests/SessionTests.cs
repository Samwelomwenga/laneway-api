using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class SessionTests : ApiTests
{
    public SessionTests(TestStack stack) : base(stack)
    {
    }

    [Theory]
    [InlineData("GET", "/api/v1/auth/sessions")]
    [InlineData("POST", "/api/v1/auth/sign-out")]
    [InlineData("POST", "/api/v1/auth/sign-out-everywhere")]
    [InlineData("POST", "/api/v1/auth/sessions/0b7f4a7e-3c55-4d7a-9a51-8e43b1f0c2d1/sign-out")]
    public async Task Each_route_needs_a_bearer_token(string method, string path)
    {
        using var response = await SendOverHttpsAsync(new HttpMethod(method), path, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.NotAuthenticated, Assert.Single((await response.ReadEnvelope<object>()).Errors!).Code);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_and_clears_the_cookie()
    {
        var (signedUp, cookie) = await SignUpOverHttpsAsync();
        var sid = SessionOf(signedUp.AccessToken);

        using var response = await SendOverHttpsAsync(HttpMethod.Post, "/api/v1/auth/sign-out", signedUp.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RefreshCookieAssert.Cleared(response);
        var session = await SessionAsync(sid);
        Assert.Equal(SessionEndReason.SignedOut, session.EndReason);
        Assert.Equal(Time.GetUtcNow().UtcDateTime, session.EndedAt!.Value, TimeSpan.FromMilliseconds(1));
        await AssertSessionDeadAsync(signedUp.AccessToken, cookie);
        AssertEnded(sid, "SignedOut");
    }

    [Fact]
    public async Task Sign_out_leaves_the_users_other_sessions()
    {
        var (signedUp, _) = await SignUpOverHttpsAsync();
        var (other, otherCookie) = await SignInOverHttpsAsync(signedUp.User.Email);

        using var response = await SendOverHttpsAsync(HttpMethod.Post, "/api/v1/auth/sign-out", signedUp.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertSessionLiveAsync(other.AccessToken, otherCookie);
    }

    [Fact]
    public async Task Signing_out_of_an_ended_session_still_succeeds_and_keeps_its_reason()
    {
        var (signedUp, _) = await SignUpOverHttpsAsync();
        var sid = SessionOf(signedUp.AccessToken);
        await EndInDatabaseAsync(sid, SessionEndReason.TokenReused);

        var reply = await FromServicesAsync(services =>
            services.GetRequiredService<IAuthService>().SignOutAsync(signedUp.User.Id, sid));

        Assert.Equal((int)HttpStatusCode.NoContent, reply.Response.StatusCode);
        Assert.Same(RefreshCookieChange.Cleared, reply.Cookie);
        Assert.Equal(SessionEndReason.TokenReused, (await SessionAsync(sid)).EndReason);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "SessionEnded");
    }

    [Fact]
    public async Task Sign_out_everywhere_ends_every_session_of_the_user_and_no_one_elses()
    {
        var (signedUp, cookie) = await SignUpOverHttpsAsync();
        var (other, otherCookie) = await SignInOverHttpsAsync(signedUp.User.Email);

        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, "/api/v1/auth/sign-out-everywhere", signedUp.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RefreshCookieAssert.Cleared(response);
        foreach (var sid in new[] { SessionOf(signedUp.AccessToken), SessionOf(other.AccessToken) })
        {
            var session = await SessionAsync(sid);
            Assert.Equal(SessionEndReason.SignedOutEverywhere, session.EndReason);
            Assert.Equal(Time.GetUtcNow().UtcDateTime, session.EndedAt!.Value, TimeSpan.FromMilliseconds(1));
            AssertEnded(sid, "SignedOutEverywhere");
        }

        await AssertSessionDeadAsync(signedUp.AccessToken, cookie);
        await AssertSessionDeadAsync(other.AccessToken, otherCookie);
        using var bystander = await GetAsync($"/api/v1/users/{ActorId}");
        Assert.Equal(HttpStatusCode.OK, bystander.StatusCode);
    }

    [Fact]
    public async Task Sign_out_everywhere_keeps_the_reason_of_a_session_that_already_ended()
    {
        var (signedUp, _) = await SignUpOverHttpsAsync();
        var (other, _) = await SignInOverHttpsAsync(signedUp.User.Email);
        await EndInDatabaseAsync(SessionOf(other.AccessToken), SessionEndReason.TokenReused);

        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, "/api/v1/auth/sign-out-everywhere", signedUp.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(SessionEndReason.TokenReused, (await SessionAsync(SessionOf(other.AccessToken))).EndReason);
        Assert.Single(Logs(), log => log.Id.Name == "SessionEnded");
    }

    [Fact]
    public async Task Sign_out_everywhere_waits_on_a_session_ending_and_keeps_its_reason()
    {
        var (signedUp, _) = await SignUpOverHttpsAsync();
        var (other, _) = await SignInOverHttpsAsync(signedUp.User.Email);
        var otherSid = SessionOf(other.AccessToken);
        await using var ending = Services.CreateAsyncScope();
        var database = ending.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Sessions
            .Where(s => s.Id == otherSid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EndedAt, Time.GetUtcNow().UtcDateTime)
                .SetProperty(x => x.EndReason, SessionEndReason.SignedOut));

        var everywhere = SendOverHttpsAsync(
            HttpMethod.Post, "/api/v1/auth/sign-out-everywhere", signedUp.AccessToken);
        await WaitForLockWaitAsync();
        await transaction.CommitAsync();
        using var response = await everywhere;

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(SessionEndReason.SignedOut, (await SessionAsync(otherSid)).EndReason);
        Assert.Equal(
            SessionEndReason.SignedOutEverywhere, (await SessionAsync(SessionOf(signedUp.AccessToken))).EndReason);
        Assert.Single(Logs(), log => log.Id.Name == "SessionEnded");
    }

    [Fact]
    public async Task The_session_list_shows_active_sessions_newest_use_first()
    {
        var (signedUp, firstCookie) = await SignUpOverHttpsAsync();
        Time.Advance(TimeSpan.FromMinutes(5));
        var (second, _) = await SignInOverHttpsAsync(signedUp.User.Email, "Mozilla/5.0 (Macintosh) Firefox/131.0");
        Time.Advance(TimeSpan.FromMinutes(5));
        (await RefreshAsync(firstCookie)).Dispose();
        var (ended, _) = await SignInOverHttpsAsync(signedUp.User.Email);
        await EndInDatabaseAsync(SessionOf(ended.AccessToken), SessionEndReason.SignedOut);
        var (idle, _) = await SignInOverHttpsAsync(signedUp.User.Email);
        await FromDatabaseAsync(database => database.Sessions
            .Where(s => s.Id == SessionOf(idle.AccessToken))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, x => x.LastUsedAt - Session.IdleTimeout)));

        using var response = await SendOverHttpsAsync(HttpMethod.Get, "/api/v1/auth/sessions", second.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = (await response.ReadEnvelope<List<SessionDto>>()).Data!;
        Assert.Equal([SessionOf(signedUp.AccessToken), SessionOf(second.AccessToken)], sessions.Select(s => s.Id));
        Assert.Equal([false, true], sessions.Select(s => s.Current));
        var stored = await SessionAsync(SessionOf(second.AccessToken));
        var listed = sessions[1];
        Assert.Equal(stored.CreatedAt, listed.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(stored.LastUsedAt, listed.LastUsedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(stored.ExpiresAt, listed.ExpiresAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal("Mozilla/5.0 (Macintosh) Firefox/131.0", listed.UserAgent);
        Assert.True(sessions[0].LastUsedAt > listed.LastUsedAt);
    }

    [Fact]
    public async Task The_session_list_shows_only_the_callers_sessions()
    {
        var (signedUp, _) = await SignUpOverHttpsAsync();

        using var response = await SendOverHttpsAsync(HttpMethod.Get, "/api/v1/auth/sessions", signedUp.AccessToken);

        var listed = Assert.Single((await response.ReadEnvelope<List<SessionDto>>()).Data!);
        Assert.Equal(SessionOf(signedUp.AccessToken), listed.Id);
    }

    [Fact]
    public async Task Ending_another_session_from_the_list_leaves_the_callers()
    {
        var (signedUp, cookie) = await SignUpOverHttpsAsync();
        var (other, otherCookie) = await SignInOverHttpsAsync(signedUp.User.Email);
        var sid = SessionOf(other.AccessToken);

        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, $"/api/v1/auth/sessions/{sid}/sign-out", signedUp.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RefreshCookieAssert.Untouched(response);
        var session = await SessionAsync(sid);
        Assert.Equal(SessionEndReason.EndedFromSessionList, session.EndReason);
        Assert.Equal(Time.GetUtcNow().UtcDateTime, session.EndedAt!.Value, TimeSpan.FromMilliseconds(1));
        await AssertSessionDeadAsync(other.AccessToken, otherCookie);
        await AssertSessionLiveAsync(signedUp.AccessToken, cookie);
        AssertEnded(sid, "EndedFromSessionList");
    }

    [Fact]
    public async Task Ending_the_callers_own_session_from_the_list_clears_the_cookie()
    {
        var (signedUp, cookie) = await SignUpOverHttpsAsync();
        var sid = SessionOf(signedUp.AccessToken);

        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, $"/api/v1/auth/sessions/{sid}/sign-out", signedUp.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RefreshCookieAssert.Cleared(response);
        Assert.Equal(SessionEndReason.EndedFromSessionList, (await SessionAsync(sid)).EndReason);
        await AssertSessionDeadAsync(signedUp.AccessToken, cookie);
    }

    [Fact]
    public async Task Ending_an_already_ended_session_from_the_list_gets_204_and_keeps_its_reason()
    {
        var (signedUp, _) = await SignUpOverHttpsAsync();
        var (other, _) = await SignInOverHttpsAsync(signedUp.User.Email);
        var sid = SessionOf(other.AccessToken);
        await EndInDatabaseAsync(sid, SessionEndReason.SignedOut);

        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, $"/api/v1/auth/sessions/{sid}/sign-out", signedUp.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RefreshCookieAssert.Untouched(response);
        Assert.Equal(SessionEndReason.SignedOut, (await SessionAsync(sid)).EndReason);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "SessionEnded");
    }

    [Fact]
    public async Task Someone_elses_session_gets_404_and_lives_on()
    {
        var (signedUp, _) = await SignUpOverHttpsAsync();

        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, $"/api/v1/auth/sessions/{ActorUser.SessionId}/sign-out", signedUp.AccessToken);

        await AssertSessionNotFoundAsync(response, ActorUser.SessionId);
        Assert.Null((await SessionAsync(ActorUser.SessionId)).EndedAt);
        using var bystander = await GetAsync($"/api/v1/users/{ActorId}");
        Assert.Equal(HttpStatusCode.OK, bystander.StatusCode);
    }

    [Fact]
    public async Task A_missing_session_gets_404()
    {
        var missing = Guid.NewGuid();

        using var response = await SendOverHttpsAsync(
            HttpMethod.Post, $"/api/v1/auth/sessions/{missing}/sign-out", ActorUser.AccessToken);

        await AssertSessionNotFoundAsync(response, missing);
    }

    private async Task<(SignedInDto SignedIn, string Cookie)> SignInOverHttpsAsync(
        string email, string userAgent = "Laneway.Api.Tests")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-in")
        {
            Content = JsonContent.Create(new { email, password = SignUpPassword })
        };
        request.Headers.UserAgent.ParseAdd(userAgent);
        using var response = await Https.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.ReadEnvelope<SignedInDto>()).Data!,
            RefreshCookieAssert.Set(response, Session.AbsoluteLifetime));
    }

    private Task<Session> SessionAsync(Guid sid) =>
        FromDatabaseAsync(database => database.Sessions.AsNoTracking().SingleAsync(s => s.Id == sid));

    private Task EndInDatabaseAsync(Guid sid, SessionEndReason reason) =>
        FromDatabaseAsync(database => database.Sessions
            .Where(s => s.Id == sid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EndedAt, Time.GetUtcNow().UtcDateTime)
                .SetProperty(x => x.EndReason, reason)));

    private void AssertEnded(Guid sid, string endReason)
    {
        var logged = Assert.Single(
            Logs(), log => log.Id.Name == "SessionEnded" && log.GetStructuredStateValue("sid") == sid.ToString());
        Assert.Equal(AuthLog.Category, logged.Category);
        Assert.Equal(endReason, logged.GetStructuredStateValue("endReason"));
    }

    private async Task AssertSessionDeadAsync(string accessToken, string cookie)
    {
        using var read = await SendOverHttpsAsync(HttpMethod.Get, "/api/v1/auth/sessions", accessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        using var refresh = await RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private async Task AssertSessionLiveAsync(string accessToken, string cookie)
    {
        using var read = await SendOverHttpsAsync(HttpMethod.Get, "/api/v1/auth/sessions", accessToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var refresh = await RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    private static async Task AssertSessionNotFoundAsync(HttpResponseMessage response, Guid sid)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        RefreshCookieAssert.Untouched(response);
        var error = Assert.Single((await response.ReadEnvelope<object>()).Errors!);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
        Assert.Equal($"Session {sid} does not exist.", error.Message);
    }
}
