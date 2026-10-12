using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class SignInBlockTests : ApiTests
{
    private const string WrongPassword = "not the password";

    public SignInBlockTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task The_5th_wrong_password_blocks_sign_in_for_1_minute()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await SignInAsync(WrongPassword)).Dispose();
        }

        using var blocked = await SignInAsync(TestUser.Password);
        Time.Advance(TimeSpan.FromSeconds(59));
        using var stillBlocked = await SignInAsync(TestUser.Password);
        Time.Advance(TimeSpan.FromSeconds(1));
        using var after = await SignInAsync(TestUser.Password);

        await AssertLooksLikeAWrongPasswordAsync(blocked);
        await AssertLooksLikeAWrongPasswordAsync(stillBlocked);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Theory]
    [InlineData(10, 2)]
    [InlineData(75, 15)]
    [InlineData(80, 15)]
    public async Task A_longer_run_of_wrong_passwords_blocks_for_longer(int failures, int minutes)
    {
        await SetFailuresAsync(failures - 1);

        (await SignInAsync(WrongPassword)).Dispose();
        Time.Advance(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(1));
        using var blocked = await SignInAsync(TestUser.Password);
        Time.Advance(TimeSpan.FromSeconds(1));
        using var after = await SignInAsync(TestUser.Password);

        await AssertLooksLikeAWrongPasswordAsync(blocked);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Attempts_during_a_block_do_not_add_to_the_count()
    {
        await SetFailuresAsync(4);
        (await SignInAsync(WrongPassword)).Dispose();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await SignInAsync(WrongPassword)).Dispose();
        }

        var user = await ActorRowAsync();
        Assert.Equal(5, user.FailedSignInCount);
        Assert.Equal(Time.GetUtcNow().UtcDateTime.AddMinutes(1), user.SignInBlockedUntil!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task The_count_starts_over_after_12_hours_with_no_failure()
    {
        await SetFailuresAsync(4);
        Time.Advance(TimeSpan.FromHours(12));

        (await SignInAsync(WrongPassword)).Dispose();
        using var response = await SignInAsync(TestUser.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_count()
    {
        await SetFailuresAsync(4);

        using var signedIn = await SignInAsync(TestUser.Password);
        (await SignInAsync(WrongPassword)).Dispose();

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        var user = await ActorRowAsync();
        Assert.Equal(1, user.FailedSignInCount);
        Assert.Null(user.SignInBlockedUntil);
    }

    [Fact]
    public async Task Wrong_passwords_sent_at_once_each_add_to_the_count()
    {
        var attempts = Enumerable.Range(0, 5).Select(_ => SignInAsync(WrongPassword));

        foreach (var response in await Task.WhenAll(attempts))
        {
            response.Dispose();
        }

        var user = await ActorRowAsync();
        Assert.Equal(5, user.FailedSignInCount);
        Assert.NotNull(user.SignInBlockedUntil);
    }

    [Fact]
    public async Task An_unknown_email_never_blocks()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await SignInAsync(WrongPassword, "nobody@example.com")).Dispose();
        }

        Assert.DoesNotContain(Logs(), log => log.Id.Name == "SignInBlockStarted");
        Assert.All(
            Logs().Where(log => log.Id.Name == "SignInFailed"),
            log => Assert.Equal("False", log.GetStructuredStateValue("blocked")));
    }

    [Fact]
    public async Task Existing_sessions_keep_working_during_a_block()
    {
        await SetFailuresAsync(4);
        (await SignInAsync(WrongPassword)).Dispose();

        using var response = await GetAsync("/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_block_logs_when_it_starts_and_marks_each_failure_made_during_it()
    {
        await SetFailuresAsync(4);
        (await SignInAsync(WrongPassword)).Dispose();
        (await SignInAsync(TestUser.Password)).Dispose();

        var started = Assert.Single(Logs(), log => log.Id.Name == "SignInBlockStarted");
        Assert.Equal(AuthLog.Category, started.Category);
        Assert.Equal(LogLevel.Warning, started.Level);
        Assert.Equal(ActorId.ToString(), started.GetStructuredStateValue("sub"));
        Assert.Equal("5", started.GetStructuredStateValue("failureCount"));
        Assert.Equal(
            Time.GetUtcNow().UtcDateTime.AddMinutes(1),
            DateTime.Parse(
                started.GetStructuredStateValue("blockedUntil")!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            TimeSpan.FromSeconds(1));
        Assert.Equal(
            ["False", "True"],
            Logs().Where(log => log.Id.Name == "SignInFailed").Select(log => log.GetStructuredStateValue("blocked")));
    }

    [Fact]
    public async Task The_meter_counts_each_block_and_each_attempt_made_during_one()
    {
        var meters = Services.GetRequiredService<IMeterFactory>();
        using var blocks = new MetricCollector<long>(meters, AuthMeter.Name, AuthMeter.SignInBlocksName);
        using var attempts = new MetricCollector<long>(meters, AuthMeter.Name, AuthMeter.SignInAttemptsName);
        await SetFailuresAsync(4);

        (await SignInAsync(WrongPassword)).Dispose();
        (await SignInAsync(TestUser.Password)).Dispose();

        Assert.Equal(1, Assert.Single(blocks.GetMeasurementSnapshot()).Value);
        Assert.Equal(
            ["wrong_password", "blocked"],
            attempts.GetMeasurementSnapshot().Select(measurement => measurement.Tags["result"]));
    }

    private async Task AssertLooksLikeAWrongPasswordAsync(HttpResponseMessage response)
    {
        using var wrong = await SignInAsync(WrongPassword, "nobody@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> SignInAsync(string password, string? email = null) =>
        SendFromBrowserAsync("/api/v1/auth/sign-in", new { email = email ?? ActorUser.User.Email, password });

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
        FromDatabaseAsync(database => database.Users.AsNoTracking().SingleAsync(user => user.Id == ActorId));
}
