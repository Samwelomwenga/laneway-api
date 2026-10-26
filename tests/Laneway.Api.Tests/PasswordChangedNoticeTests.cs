using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class PasswordChangedNoticeTests : ApiTests
{
    private const string Firefox =
        "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0";

    public PasswordChangedNoticeTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task Change_password_emails_the_user_a_notice()
    {
        await FromDatabaseAsync(async database =>
        {
            await database.Users.Where(u => u.Id == ActorId)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.TimeZone, "Africa/Nairobi"));
            return await database.Sessions.Where(s => s.Id == ActorUser.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.UserAgent, Firefox));
        });
        using var response = await ChangePasswordAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var send = await AwaitEmailAsync(ActorUser.User.Email);
        Assert.Equal("Your Laneway password was changed", send.Subject);
        Assert.Contains(
            "East Africa Time from Firefox on Linux. We signed you out everywhere else.",
            send.Text,
            StringComparison.Ordinal);
        Assert.Contains($"{ApiFactory.AllowedOrigin}/forgot-password", send.Text, StringComparison.Ordinal);
        Assert.Equal(["PasswordChanged"], send.Tags);
        Assert.Equal(ApiFactory.BrevoApiKey, send.ApiKey);
        Assert.Equal("Laneway", send.SenderName);
        Assert.Equal(ApiFactory.SenderEmail, send.SenderEmail);
        Assert.Equal(new Uri("https://api.brevo.com/v3/smtp/email"), send.Uri);
    }

    [Fact]
    public async Task A_sent_delivery_keeps_brevos_message_id()
    {
        using var response = await ChangePasswordAsync();

        var send = await AwaitEmailAsync(ActorUser.User.Email);
        var delivery = await AwaitDeliveryAsync(DeliveryStatus.Sent);
        Assert.Equal(send.MessageId, delivery.ProviderMessageId);
        Assert.Equal(MessageType.PasswordChanged, delivery.Type);
        Assert.Equal(MessageChannel.Email, delivery.Channel);
        Assert.Equal(delivery.IdempotencyKey.ToString(), send.IdempotencyKey);
    }

    [Fact]
    public async Task A_sent_delivery_logs_without_the_address()
    {
        using var response = await ChangePasswordAsync();

        var delivery = await AwaitDeliveryAsync(DeliveryStatus.Sent);
        var sent = Assert.Single(Logs(), log => log.Id.Name == "DeliverySent");
        Assert.Equal(MessageLog.Category, sent.Category);
        Assert.Equal(LogLevel.Information, sent.Level);
        Assert.Equal(delivery.ProviderMessageId, sent.GetStructuredStateValue("providerMessageId"));
        Assert.Equal(delivery.Id.ToString(), sent.GetStructuredStateValue("deliveryId"));
        Assert.Equal("PasswordChanged", sent.GetStructuredStateValue("messageType"));
        Assert.Equal("Email", sent.GetStructuredStateValue("channel"));
        Assert.Equal("1", sent.GetStructuredStateValue("attempt"));
        Assert.Equal(ActorId.ToString(), sent.GetStructuredStateValue("sub"));
        Assert.DoesNotContain(
            Logs(), log => log.Message.Contains(ActorUser.User.Email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_wrong_password_sends_nothing()
    {
        using var response = await PostAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = "not the password", newPassword = "quiet harbor lantern" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await FromDatabaseAsync(database => database.Deliveries.CountAsync()));
    }

    private Task<HttpResponseMessage> ChangePasswordAsync() =>
        PostAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = TestUser.Password, newPassword = "quiet harbor lantern" });
}
