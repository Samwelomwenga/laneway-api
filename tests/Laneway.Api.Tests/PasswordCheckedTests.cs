using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class PasswordCheckedTests : ApiTests
{
    private const string WrongPassword = "not the password";

    private const string NewPassword = "quiet harbor lantern";

    public PasswordCheckedTests(TestStack stack) : base(stack)
    {
    }

    public static TheoryData<string> Endpoints => ["change-password", "change-email", "delete-user"];

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Each_route_needs_a_bearer_token(string endpoint)
    {
        using var response = await SendOverHttpsAsync(HttpMethod.Post, $"/api/v1/auth/{endpoint}", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Change_password_swaps_the_password_and_keeps_the_callers_session()
    {
        using var response = await ChangePasswordAsync(TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
        using var oldPassword = await SignInAsync(TestUser.Password);
        using var newPassword = await SignInAsync(NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
        using var profile = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var changed = Assert.Single(Logs(), log => log.Id.Name == "PasswordChanged");
        Assert.Equal(AuthLog.Category, changed.Category);
        Assert.Equal(LogLevel.Information, changed.Level);
        Assert.Equal(ActorId.ToString(), changed.GetStructuredStateValue("sub"));
        Assert.Equal(ActorUser.SessionId.ToString(), changed.GetStructuredStateValue("sid"));
    }

    [Fact]
    public async Task Change_password_ends_every_other_session()
    {
        var other = await SignInForTokenAsync();

        using var response = await ChangePasswordAsync(TestUser.Password, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var otherRead = await SendOverHttpsAsync(HttpMethod.Get, "/api/v1/auth/sessions", other);
        Assert.Equal(HttpStatusCode.Unauthorized, otherRead.StatusCode);
        var session = await SessionAsync(SessionOf(other));
        Assert.Equal(SessionEndReason.PasswordChanged, session.EndReason);
        Assert.Null((await SessionAsync(ActorUser.SessionId)).EndedAt);
        var ended = Assert.Single(Logs(), log => log.Id.Name == "SessionEnded");
        Assert.Equal(SessionOf(other).ToString(), ended.GetStructuredStateValue("sid"));
        Assert.Equal("PasswordChanged", ended.GetStructuredStateValue("endReason"));
    }

    [Fact]
    public async Task Change_password_refuses_the_current_password_as_the_new_one()
    {
        using var response = await ChangePasswordAsync(TestUser.Password, TestUser.Password);

        await AssertInvalidAsync(response, "newPassword", ErrorCodes.SameAsCurrent);
    }

    [Fact]
    public async Task Change_password_checks_the_new_password_against_the_callers_username()
    {
        using var response = await ChangePasswordAsync(TestUser.Password, $"{ActorUser.User.Username} lantern");

        await AssertInvalidAsync(response, "newPassword", ErrorCodes.ContainsContextWord);
    }

    [Fact]
    public async Task Change_password_checks_the_new_passwords_length()
    {
        using var response = await ChangePasswordAsync(TestUser.Password, "short");

        await AssertInvalidAsync(response, "newPassword", ErrorCodes.TooShort);
    }

    [Fact]
    public async Task Change_email_lowercases_the_new_email_and_ends_no_sessions()
    {
        var other = await SignInForTokenAsync();
        var newEmail = NewEmail();

        using var response = await PostAsync(
            "/api/v1/auth/change-email", new { password = TestUser.Password, newEmail = $"  {newEmail.ToUpperInvariant()} " });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var signIn = await SignInAsync(TestUser.Password, newEmail);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        using var otherRead = await SendOverHttpsAsync(HttpMethod.Get, "/api/v1/auth/sessions", other);
        Assert.Equal(HttpStatusCode.OK, otherRead.StatusCode);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "SessionEnded");
        var changed = Assert.Single(Logs(), log => log.Id.Name == "EmailChanged");
        Assert.Equal(ActorId.ToString(), changed.GetStructuredStateValue("sub"));
        Assert.Equal(ActorUser.SessionId.ToString(), changed.GetStructuredStateValue("sid"));
        Assert.DoesNotContain(newEmail, changed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Change_email_refuses_an_email_another_user_has()
    {
        var other = await CreateUserAsync();

        using var response = await PostAsync(
            "/api/v1/auth/change-email", new { password = TestUser.Password, newEmail = other.User.Email });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal("newEmail", error.Field);
        Assert.Equal(ErrorCodes.Duplicate, error.Code);
    }

    [Fact]
    public async Task Delete_user_deletes_the_caller_and_ends_every_session()
    {
        var other = await SignInForTokenAsync();

        using var response = await PostAsync("/api/v1/auth/delete-user", new { password = TestUser.Password });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var profile = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        using var signIn = await SignInAsync(TestUser.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        var user = await FromDatabaseAsync(database =>
            database.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == ActorId));
        Assert.Equal(Time.GetUtcNow().UtcDateTime, user.DeletedAt!.Value, TimeSpan.FromMilliseconds(1));
        foreach (var sid in new[] { ActorUser.SessionId, SessionOf(other) })
        {
            Assert.Equal(SessionEndReason.UserDeleted, (await SessionAsync(sid)).EndReason);
        }

        Assert.Equal(
            ["UserDeleted", "UserDeleted"],
            Logs().Where(log => log.Id.Name == "SessionEnded").Select(log => log.GetStructuredStateValue("endReason")));
        var deleted = Assert.Single(Logs(), log => log.Id.Name == "UserDeleted");
        Assert.Equal(ActorId.ToString(), deleted.GetStructuredStateValue("sub"));
        Assert.Equal(ActorUser.SessionId.ToString(), deleted.GetStructuredStateValue("sid"));
    }

    [Fact]
    public async Task Delete_user_clears_the_refresh_cookie()
    {
        var (signedUp, cookie) = await SignUpOverHttpsAsync();

        using var response = await SendWithTokenAsync(
            HttpMethod.Post, "/api/v1/auth/delete-user", signedUp.AccessToken, new { password = SignUpPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        RefreshCookieAssert.Cleared(response);
        using var refresh = await RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task The_old_user_delete_route_is_gone()
    {
        using var response = await DeleteAsync($"/api/v1/users/{ActorId}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task A_wrong_password_is_a_400_on_the_password_field_and_counts_toward_the_block(string endpoint)
    {
        using var response = await CheckAsync(endpoint, WrongPassword);

        await AssertInvalidAsync(response, PasswordField(endpoint), ErrorCodes.WrongPassword);
        Assert.Equal(1, (await ActorRowAsync()).FailedSignInCount);
        var failed = Assert.Single(Logs(), log => log.Id.Name == "PasswordCheckFailed");
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal(ActorId.ToString(), failed.GetStructuredStateValue("sub"));
        Assert.Equal(ActorUser.SessionId.ToString(), failed.GetStructuredStateValue("sid"));
        Assert.Equal("False", failed.GetStructuredStateValue("blocked"));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task A_right_password_resets_the_count(string endpoint)
    {
        await SetFailuresAsync(4);

        using var response = await CheckAsync(endpoint, TestUser.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await ActorRowAsync();
        Assert.Equal(0, user.FailedSignInCount);
        Assert.Null(user.LastFailedSignInAt);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task A_5th_wrong_password_here_blocks_sign_in(string endpoint)
    {
        await SetFailuresAsync(4);

        (await CheckAsync(endpoint, WrongPassword)).Dispose();
        using var signIn = await SignInAsync(TestUser.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        Assert.Single(Logs(), log => log.Id.Name == "SignInBlockStarted");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task A_block_from_sign_in_returns_429_here_even_for_the_right_password(string endpoint)
    {
        await SetFailuresAsync(4);
        (await SignInAsync(WrongPassword)).Dispose();
        Time.Advance(TimeSpan.FromSeconds(20.5));

        using var response = await CheckAsync(endpoint, TestUser.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(40), response.Headers.RetryAfter!.Delta);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(ErrorCodes.TooManyRequests, error.Code);
        Assert.Null(error.Field);
        var user = await ActorRowAsync();
        Assert.Equal(5, user.FailedSignInCount);
        Assert.Null(user.DeletedAt);
        var failed = Assert.Single(Logs(), log => log.Id.Name == "PasswordCheckFailed");
        Assert.Equal("True", failed.GetStructuredStateValue("blocked"));
    }

    [Fact]
    public async Task The_meter_counts_failures_by_endpoint_and_block()
    {
        var meters = Services.GetRequiredService<IMeterFactory>();
        using var failures = new MetricCollector<long>(meters, AuthMeter.Name, AuthMeter.PasswordCheckFailuresName);

        (await CheckAsync("change-password", WrongPassword)).Dispose();
        (await CheckAsync("change-email", WrongPassword)).Dispose();
        await SetFailuresAsync(4);
        (await CheckAsync("delete-user", WrongPassword)).Dispose();
        (await CheckAsync("delete-user", TestUser.Password)).Dispose();

        Assert.Equal(
            [("change_password", false), ("change_email", false), ("delete_user", false), ("delete_user", true)],
            failures.GetMeasurementSnapshot().Select(m => ((string)m.Tags["endpoint"]!, (bool)m.Tags["blocked"]!)));
    }

    [Fact]
    public async Task A_right_password_rehashes_an_old_hash()
    {
        var weaker = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 100_000 }))
            .HashPassword(null!, TestUser.Password);
        await FromDatabaseAsync(database => database.Users
            .Where(u => u.Id == ActorId)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.PasswordHash, weaker)));

        using var response = await CheckAsync("change-email", TestUser.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await ActorRowAsync();
        Assert.NotEqual(weaker, user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = Passwords.Iterations }))
                .VerifyHashedPassword(user, user.PasswordHash, TestUser.Password));
    }

    private Task<HttpResponseMessage> CheckAsync(string endpoint, string password) => endpoint switch
    {
        "change-password" => ChangePasswordAsync(password, NewPassword),
        "change-email" => PostAsync("/api/v1/auth/change-email", new { password, newEmail = NewEmail() }),
        "delete-user" => PostAsync("/api/v1/auth/delete-user", new { password }),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null)
    };

    private static string PasswordField(string endpoint) =>
        endpoint == "change-password" ? "currentPassword" : "password";

    private Task<HttpResponseMessage> ChangePasswordAsync(string currentPassword, string newPassword) =>
        PostAsync("/api/v1/auth/change-password", new { currentPassword, newPassword });

    private Task<HttpResponseMessage> SignInAsync(string password, string? email = null) =>
        SendFromBrowserAsync("/api/v1/auth/sign-in", new { email = email ?? ActorUser.User.Email, password });

    private async Task<string> SignInForTokenAsync()
    {
        using var response = await SignInAsync(TestUser.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadEnvelope<SignedInDto>()).Data!.AccessToken;
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response, string field, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single(await response.ReadErrors());
        Assert.Equal(field, error.Field);
        Assert.Equal(code, error.Code);
    }

    private Task SetFailuresAsync(int count)
    {
        var now = Time.GetUtcNow().UtcDateTime;
        return FromDatabaseAsync(database => database.Users
            .Where(user => user.Id == ActorId)
            .ExecuteUpdateAsync(user => user
                .SetProperty(u => u.FailedSignInCount, count)
                .SetProperty(u => u.LastFailedSignInAt, now)));
    }

    private Task<User> ActorRowAsync() =>
        FromDatabaseAsync(database =>
            database.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(user => user.Id == ActorId));

    private Task<Session> SessionAsync(Guid sid) =>
        FromDatabaseAsync(database => database.Sessions.AsNoTracking().SingleAsync(s => s.Id == sid));
}
