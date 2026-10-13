using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class SignInTests : ApiTests
{
    public SignInTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_new_user_signs_up_and_writes_with_the_returned_token()
    {
        using var response = await SignUpAsync(SignUpBody("Ana@Example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var signedUp = (await response.ReadEnvelope<SignedUpDto>()).Data!;
        Assert.Equal("ana@example.com", signedUp.User.Email);
        Assert.Single(signedUp.User.Accounts);
        Assert.Equal(Time.GetUtcNow().UtcDateTime.AddMinutes(15), signedUp.AccessTokenExpiresAt, TimeSpan.FromSeconds(1));
        Assert.Equal(Time.GetUtcNow().UtcDateTime.AddDays(30), signedUp.SessionExpiresAt, TimeSpan.FromSeconds(1));

        using var write = await SendWithTokenAsync(
            HttpMethod.Post, "/api/v1/workspaces", signedUp.AccessToken, new { name = "Design", visibility = "Public" });

        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        Assert.Equal(signedUp.User.Id, (await write.ReadEnvelope<WorkSpaceDto>()).Data!.CreatedBy);
    }

    [Fact]
    public async Task Sign_up_logs_the_new_user_and_session()
    {
        using var response = await SignUpAsync(SignUpBody("ana@example.com"));
        var signedUp = (await response.ReadEnvelope<SignedUpDto>()).Data!;

        var logged = Assert.Single(Logs(), log => log.Id.Name == "SignUp");
        Assert.Equal(AuthLog.Category, logged.Category);
        Assert.Equal(signedUp.User.Id.ToString(), logged.GetStructuredStateValue("sub"));
        Assert.Equal(await OnlySessionIdAsync(signedUp.User.Id), logged.GetStructuredStateValue("sid"));
    }

    [Fact]
    public async Task A_taken_username_or_email_still_gets_409()
    {
        using var first = await SignUpAsync(SignUpBody("ana@example.com", username: "ana"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var response = await SignUpAsync(SignUpBody(" ANA@example.com", username: "ana"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ["email", "username"],
            (await response.ReadEnvelope<object>()).Errors!.Select(error => error.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_user_signs_in_with_their_email_in_any_case()
    {
        using var signUp = await SignUpAsync(SignUpBody("Ana.Waweru@Example.com"));
        var userId = (await signUp.ReadEnvelope<SignedUpDto>()).Data!.User.Id;

        using var response = await SignInAsync("  ANA.waweru@example.COM ", SignUpPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.False(JsonDocument.Parse(raw).RootElement.GetProperty("data").TryGetProperty("user", out _));
        var signedIn = (await response.ReadEnvelope<SignedInDto>()).Data!;
        Assert.Equal(Time.GetUtcNow().UtcDateTime.AddDays(30), signedIn.SessionExpiresAt, TimeSpan.FromSeconds(1));

        using var write = await SendWithTokenAsync(
            HttpMethod.Post, "/api/v1/workspaces", signedIn.AccessToken, new { name = "Design", visibility = "Public" });
        Assert.Equal(userId, (await write.ReadEnvelope<WorkSpaceDto>()).Data!.CreatedBy);

        var logged = Assert.Single(Logs(), log => log.Id.Name == "SignInSucceeded");
        Assert.Equal(userId.ToString(), logged.GetStructuredStateValue("sub"));
        Assert.NotNull(logged.GetStructuredStateValue("sid"));
    }

    [Fact]
    public async Task Each_sign_in_starts_a_new_session_with_the_user_agent_cut_to_256_characters()
    {
        var agent = new string('a', 300);

        using var response = await SignInAsync(ActorUser.User.Email, TestUser.Password, agent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = await FromDatabaseAsync(database => database.Sessions
            .Where(session => session.UserId == ActorId && session.Id != ActorUser.SessionId)
            .ToListAsync());
        Assert.Equal(new string('a', 256), Assert.Single(sessions).UserAgent);
    }

    [Fact]
    public async Task An_unknown_email_and_a_wrong_password_get_the_same_401()
    {
        using var unknown = await SignInAsync("nobody@example.com", TestUser.Password);
        using var wrong = await SignInAsync(ActorUser.User.Email, "not the password");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.Equal(unknownBody, await wrong.Content.ReadAsStringAsync());
        Assert.Empty(unknown.Headers.WwwAuthenticate);
        var envelope = await unknown.ReadEnvelope<object>();
        Assert.Equal("Email or password is incorrect.", envelope.Message);
        var error = Assert.Single(envelope.Errors!);
        Assert.Equal(ErrorCodes.InvalidCredentials, error.Code);
        Assert.Null(error.Field);
    }

    [Fact]
    public async Task A_failed_sign_in_logs_the_user_or_that_no_account_matched_and_never_the_email()
    {
        using var unknown = await SignInAsync("nobody@example.com", TestUser.Password);
        using var wrong = await SignInAsync(ActorUser.User.Email, "not the password");

        var failures = Logs().Where(log => log.Id.Name == "SignInFailed").ToList();
        Assert.Equal(2, failures.Count);
        Assert.All(failures, log => Assert.Equal(LogLevel.Warning, log.Level));
        Assert.Equal("False", failures[0].GetStructuredStateValue("accountFound"));
        Assert.Null(failures[0].GetStructuredStateValue("sub"));
        Assert.Equal(ActorId.ToString(), failures[1].GetStructuredStateValue("sub"));
        Assert.DoesNotContain(Logs(), log =>
            log.Message.Contains("example.com", StringComparison.Ordinal)
            || (log.StructuredState ?? []).Any(pair => pair.Value?.Contains("example.com", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task The_attempts_counter_counts_each_result()
    {
        using var attempts = new MetricCollector<long>(
            Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            AuthMeter.Name,
            AuthMeter.SignInAttemptsName);

        (await SignInAsync(ActorUser.User.Email, TestUser.Password)).Dispose();
        (await SignInAsync(ActorUser.User.Email, "not the password")).Dispose();
        (await SignInAsync("nobody@example.com", TestUser.Password)).Dispose();

        Assert.Equal(
            ["succeeded", "wrong_password", "unknown_account"],
            attempts.GetMeasurementSnapshot().Select(measurement => measurement.Tags["result"]));
        Assert.All(attempts.GetMeasurementSnapshot(), measurement => Assert.Equal(1, measurement.Value));
    }

    [Theory]
    [InlineData(7, ErrorCodes.TooShort)]
    [InlineData(129, ErrorCodes.TooLong)]
    public async Task Sign_up_rejects_a_password_outside_8_to_128_characters(int length, string code)
    {
        using var response = await SignUpAsync(SignUpBody("ana@example.com", PasswordOfLength(length)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single((await response.ReadEnvelope<object>()).Errors!);
        Assert.Equal("password", error.Field);
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public async Task Sign_up_accepts_a_password_of_8_or_128_characters(int length)
    {
        using var response = await SignUpAsync(SignUpBody("ana@example.com", PasswordOfLength(length)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Password_length_counts_code_points_not_utf16_units()
    {
        var sevenEmoji = string.Concat(Enumerable.Repeat("🔑", 7));
        var hundredTwentyEightEmoji = string.Concat(Enumerable.Repeat("🔑", 128));

        using var tooShort = await SignUpAsync(SignUpBody("ana@example.com", sevenEmoji));
        using var longest = await SignUpAsync(SignUpBody("ben@example.com", hundredTwentyEightEmoji));

        Assert.Equal(ErrorCodes.TooShort, Assert.Single((await tooShort.ReadEnvelope<object>()).Errors!).Code);
        Assert.Equal(HttpStatusCode.Created, longest.StatusCode);
    }

    [Fact]
    public async Task Passwords_are_nfkc_normalized_and_never_trimmed()
    {
        using var signUp = await SignUpAsync(SignUpBody("ana@example.com", " ﬁve plum tractors "));
        Assert.Equal(HttpStatusCode.Created, signUp.StatusCode);

        using var expanded = await SignInAsync("ana@example.com", " five plum tractors ");
        using var ligature = await SignInAsync("ana@example.com", " \uFB01ve plum tractors ");
        using var trimmed = await SignInAsync("ana@example.com", "five plum tractors");

        Assert.Equal(HttpStatusCode.OK, expanded.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ligature.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, trimmed.StatusCode);
    }

    [Fact]
    public async Task Sign_in_skips_the_minimum_length()
    {
        await SetPasswordHashAsync(ActorId, new Passwords().Hash("short"));

        using var response = await SignInAsync(ActorUser.User.Email, "short");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_rejects_a_password_over_128_characters()
    {
        using var response = await SignInAsync(ActorUser.User.Email, new string('k', 129));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single((await response.ReadEnvelope<object>()).Errors!);
        Assert.Equal("password", error.Field);
        Assert.Equal(ErrorCodes.TooLong, error.Code);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "SignInFailed");
    }

    [Fact]
    public async Task Sign_in_rehashes_a_password_stored_at_a_lower_iteration_count()
    {
        var older = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 100_000 }));
        await SetPasswordHashAsync(ActorId, older.HashPassword(null!, TestUser.Password));
        Assert.Equal(100_000, await StoredIterationCountAsync(ActorId));

        using var response = await SignInAsync(ActorUser.User.Email, TestUser.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Passwords.Iterations, await StoredIterationCountAsync(ActorId));
        using var again = await SignInAsync(ActorUser.User.Email, TestUser.Password);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task A_password_with_a_lone_surrogate_is_rejected_before_normalization()
    {
        using var response = await SendFromBrowserAsync(
            "/api/v1/auth/sign-in",
            new StringContent(
                """{"email":"ana@example.com","password":"plum\ud800tractor"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = Assert.Single((await response.ReadEnvelope<object>()).Errors!);
        Assert.Equal("password", error.Field);
        Assert.Equal(ErrorCodes.InvalidFormat, error.Code);
    }

    private static string PasswordOfLength(int length) =>
        string.Concat(Enumerable.Repeat("plum tractor ", 10))[..length];

    private Task<HttpResponseMessage> SignInAsync(string email, string password, string? userAgent = null) =>
        SendFromBrowserAsync("/api/v1/auth/sign-in", new { email, password }, userAgent: userAgent);

    private Task<string> OnlySessionIdAsync(Guid userId) =>
        FromDatabaseAsync(async database =>
            (await database.Sessions.SingleAsync(session => session.UserId == userId)).Id.ToString());

    private Task SetPasswordHashAsync(Guid userId, string hash) =>
        FromDatabaseAsync(database => database.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(user => user.SetProperty(u => u.PasswordHash, hash)));

    // Identity's V3 format: a 0x01 marker, then the PRF and the iteration count as big-endian uint32s.
    private Task<int> StoredIterationCountAsync(Guid userId) =>
        FromDatabaseAsync(async database =>
        {
            var hash = await database.Users.Where(user => user.Id == userId).Select(user => user.PasswordHash).SingleAsync();
            return (int)BinaryPrimitives.ReadUInt32BigEndian(Convert.FromBase64String(hash).AsSpan(5, 4));
        });
}

public class SessionStartTests
{
    [Fact]
    public void A_user_agent_cut_never_splits_a_surrogate_pair()
    {
        var agent = new string('a', 255) + "🔑" + "tail";

        var session = Session.Start(Guid.NewGuid(), agent, DateTime.UtcNow);

        Assert.Equal(new string('a', 255), session.UserAgent);
    }
}
