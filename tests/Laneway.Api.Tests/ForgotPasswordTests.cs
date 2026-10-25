using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ForgotPasswordTests : ApiTests
{
    public ForgotPasswordTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Forgot_password_emails_a_verified_owner_a_reset_link()
    {
        using var response = await ForgotPasswordAsync(ActorUser.User.Email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var send = await AwaitEmailAsync(ActorUser.User.Email);
        Assert.Equal("Reset your Laneway password", send.Subject);
        Assert.Contains(
            $"Someone asked to reset the password for @{ActorUser.User.Username}.", send.Text, StringComparison.Ordinal);
        Assert.Contains(
            "The link works for 1 hour. If it wasn't you, ignore this email. Your password hasn't changed.",
            send.Text,
            StringComparison.Ordinal);
        Assert.NotEmpty(send.TokenFrom(AppRoutes.ResetPassword));
        Assert.Equal(["ResetPassword"], send.Tags);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_address_answers_the_same_and_logs_why()
    {
        using var known = await ForgotPasswordAsync(ActorUser.User.Email);
        using var response = await ForgotPasswordAsync(NewEmail());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal(AuthLog.Category, skipped.Category);
        Assert.Equal(LogLevel.Information, skipped.Level);
        Assert.Equal("ResetPassword", skipped.GetStructuredStateValue("messageType"));
        Assert.Equal("unknownAccount", skipped.GetStructuredStateValue("reason"));
        Assert.Null(skipped.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task Forgot_password_for_an_unverified_address_sends_nothing_and_logs_why()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));

        using var response = await ForgotPasswordAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(await FromDatabaseAsync(database =>
            database.Deliveries.AnyAsync(d => d.Type == MessageType.ResetPassword)));
        Assert.False(await FromDatabaseAsync(database =>
            database.AuthTokens.AnyAsync(t => t.Kind == AuthTokenKind.Reset)));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal("unverified", skipped.GetStructuredStateValue("reason"));
        Assert.NotNull(skipped.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task Forgot_password_for_a_suppressed_address_sends_nothing_and_logs_why()
    {
        await SuppressAsync(ActorUser.User.Email);

        using var response = await ForgotPasswordAsync(ActorUser.User.Email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(await FromDatabaseAsync(database => database.Deliveries.AnyAsync()));
        Assert.False(await FromDatabaseAsync(database => database.AuthTokens.AnyAsync()));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal("suppressed", skipped.GetStructuredStateValue("reason"));
        Assert.Equal(ActorId.ToString(), skipped.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task Asking_changes_nothing_on_the_account()
    {
        var before = await FromDatabaseAsync(database => database.Users.AsNoTracking().SingleAsync(u => u.Id == ActorId));

        using var response = await ForgotPasswordAsync(ActorUser.User.Email);

        var after = await FromDatabaseAsync(database => database.Users.AsNoTracking().SingleAsync(u => u.Id == ActorId));
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        using var profile = await GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task The_sixth_reset_email_in_24_hours_changes_nothing_and_warns()
    {
        for (var ask = 0; ask < 5; ask++)
        {
            using var sent = await ForgotPasswordAsync(ActorUser.User.Email);
        }

        var fifth = (await Brevo.AwaitSendsAsync(5))[4].TokenFrom(AppRoutes.ResetPassword);

        using var capped = await ForgotPasswordAsync(ActorUser.User.Email);

        Assert.Equal(HttpStatusCode.Accepted, capped.StatusCode);
        Assert.Equal(5, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal(LogLevel.Warning, skipped.Level);
        Assert.Equal("addressCapped", skipped.GetStructuredStateValue("reason"));
        using var reset = await ResetPasswordAsync(fifth, "quiet harbor lantern");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }
}
