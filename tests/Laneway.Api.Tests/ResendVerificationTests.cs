using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class ResendVerificationTests : ApiTests
{
    public ResendVerificationTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Resend_sends_a_fresh_link_and_the_old_one_stops_working()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        var first = await VerifyTokenSentToAsync(email);

        using var response = await ResendVerificationAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var second = (await Brevo.AwaitSendsAsync(2))[1].TokenFrom(AppRoutes.VerifyEmail);
        using var stale = await VerifyEmailAsync(first);
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        using var verified = await VerifyEmailAsync(second);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    [Fact]
    public async Task Resend_to_an_unknown_address_answers_202_sends_nothing_and_logs_why()
    {
        using var response = await ResendVerificationAsync(NewEmail());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(await FromDatabaseAsync(database => database.Deliveries.AnyAsync()));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal(AuthLog.Category, skipped.Category);
        Assert.Equal(LogLevel.Information, skipped.Level);
        Assert.Equal("VerifyEmail", skipped.GetStructuredStateValue("messageType"));
        Assert.Equal("unknownAccount", skipped.GetStructuredStateValue("reason"));
        Assert.Null(skipped.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task Resend_to_a_verified_users_address_answers_202_sends_nothing_and_logs_why()
    {
        using var unknown = await ResendVerificationAsync(NewEmail());
        using var response = await ResendVerificationAsync(ActorUser.User.Email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        Assert.False(await FromDatabaseAsync(database => database.Deliveries.AnyAsync()));
        var skipped = Logs().Last(log => log.Id.Name == "MessageSkipped");
        Assert.Equal("verified", skipped.GetStructuredStateValue("reason"));
        Assert.Equal(ActorId.ToString(), skipped.GetStructuredStateValue("sub"));
    }

    [Fact]
    public async Task Resend_to_a_suppressed_address_sends_nothing_and_keeps_the_old_link()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        var first = await VerifyTokenSentToAsync(email);
        await SuppressAsync(email);

        using var response = await ResendVerificationAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal("suppressed", skipped.GetStructuredStateValue("reason"));
        Assert.NotNull(skipped.GetStructuredStateValue("sub"));
        using var verified = await VerifyEmailAsync(first);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    [Fact]
    public async Task The_sixth_token_email_in_24_hours_changes_nothing_and_warns()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        for (var resend = 0; resend < 4; resend++)
        {
            using var sent = await ResendVerificationAsync(email);
        }

        var fifth = (await Brevo.AwaitSendsAsync(5))[4].TokenFrom(AppRoutes.VerifyEmail);

        using var capped = await ResendVerificationAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, capped.StatusCode);
        Assert.Equal(5, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
        var skipped = Assert.Single(Logs(), log => log.Id.Name == "MessageSkipped");
        Assert.Equal(LogLevel.Warning, skipped.Level);
        Assert.Equal("addressCapped", skipped.GetStructuredStateValue("reason"));
        using var verified = await VerifyEmailAsync(fifth);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }

    [Fact]
    public async Task The_cap_counts_only_the_last_24_hours()
    {
        var email = NewEmail();
        using var signUp = await SignUpAsync(SignUpBody(email));
        for (var resend = 0; resend < 4; resend++)
        {
            using var sent = await ResendVerificationAsync(email);
        }

        Time.Advance(AddressCap.Window);

        using var response = await ResendVerificationAsync(email);

        Assert.Equal(6, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "MessageSkipped");
    }
}
