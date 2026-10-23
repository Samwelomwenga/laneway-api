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
public class DeliveryDropTests : ApiTests
{
    private const string Secret = "raw-token-that-must-not-outlive-the-delivery";

    public DeliveryDropTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_delivery_past_its_send_by_expires_without_calling_brevo()
    {
        using var deliveries = DeliveryMeter();
        Brevo.Answer(HttpStatusCode.TooManyRequests);
        await AddMessageAsync(PasswordNotice());
        await AwaitLogsAsync("DeliveryRetrying", 1);
        var sendBy = Time.GetUtcNow().UtcDateTime.AddHours(24);

        AdvanceDeliveries(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        Assert.Single(Brevo.Sends);
        var expired = Assert.Single(await AwaitLogsAsync("DeliveryExpired", 1));
        Assert.Equal(MessageLog.Category, expired.Category);
        Assert.Equal(LogLevel.Warning, expired.Level);
        Assert.Equal(
            sendBy,
            DateTime.Parse(
                expired.GetStructuredStateValue("sendBy")!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            TimeSpan.FromSeconds(1));
        Assert.Equal(["expired"], deliveries.GetMeasurementSnapshot().Select(m => m.Tags["result"]));
    }

    [Fact]
    public async Task A_delivery_for_a_user_deleted_after_it_was_added_drops()
    {
        Brevo.Answer(HttpStatusCode.TooManyRequests);
        await AddMessageAsync(PasswordNotice());
        await AwaitLogsAsync("DeliveryRetrying", 1);
        await FromDatabaseAsync(database => database.Users.Where(u => u.Id == ActorId)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.DeletedAt, Time.GetUtcNow().UtcDateTime)));

        AdvanceDeliveries(TimeSpan.FromSeconds(30));

        await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        Assert.Single(Brevo.Sends);
        var dropped = Assert.Single(await AwaitLogsAsync("DeliveryDropped", 1));
        Assert.Equal(LogLevel.Information, dropped.Level);
        Assert.Equal("userDeleted", dropped.GetStructuredStateValue("reason"));
    }

    [Fact]
    public async Task A_delivery_for_a_user_id_with_no_row_saves_and_drops()
    {
        using var deliveries = DeliveryMeter();

        await AddMessageAsync(PasswordNotice(Guid.NewGuid()));

        await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        Assert.Empty(Brevo.Sends);
        var dropped = Assert.Single(await AwaitLogsAsync("DeliveryDropped", 1));
        Assert.Equal("userDeleted", dropped.GetStructuredStateValue("reason"));
        Assert.Equal(["dropped"], deliveries.GetMeasurementSnapshot().Select(m => m.Tags["result"]));
    }

    [Fact]
    public async Task An_expired_delivery_for_a_deleted_user_logs_the_expiry()
    {
        Brevo.Answer(HttpStatusCode.TooManyRequests);
        await AddMessageAsync(PasswordNotice());
        await AwaitLogsAsync("DeliveryRetrying", 1);
        await FromDatabaseAsync(database => database.Users.Where(u => u.Id == ActorId)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.DeletedAt, Time.GetUtcNow().UtcDateTime)));

        AdvanceDeliveries(TimeSpan.FromDays(2));

        await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        await AwaitLogsAsync("DeliveryExpired", 1);
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "DeliveryDropped");
    }

    [Fact]
    public async Task A_delivery_with_no_address_on_its_channel_drops()
    {
        await SeedDeliveryAsync(userId: null, address: null);

        var delivery = await AwaitDeliveryAsync(DeliveryStatus.Dropped);
        Assert.Empty(Brevo.Sends);
        Assert.Null(delivery.Secret);
        var dropped = Assert.Single(await AwaitLogsAsync("DeliveryDropped", 1));
        Assert.Equal("noAddress", dropped.GetStructuredStateValue("reason"));
    }

    [Fact]
    public async Task A_sent_delivery_clears_its_secret()
    {
        await SeedDeliveryAsync(ActorId, address: null);

        var delivery = await AwaitDeliveryAsync(DeliveryStatus.Sent);
        Assert.Null(delivery.Secret);
    }

    [Fact]
    public async Task A_failed_delivery_clears_its_secret()
    {
        Brevo.Answer(HttpStatusCode.BadRequest);
        await SeedDeliveryAsync(ActorId, address: null);

        var delivery = await AwaitDeliveryAsync(DeliveryStatus.Failed);
        Assert.Null(delivery.Secret);
    }

    [Fact]
    public async Task A_retrying_delivery_keeps_its_secret()
    {
        Brevo.Answer(HttpStatusCode.TooManyRequests);
        var id = await SeedDeliveryAsync(ActorId, address: null);

        await AwaitLogsAsync("DeliveryRetrying", 1);
        Assert.Equal(
            Secret,
            await FromDatabaseAsync(database =>
                database.Deliveries.Where(d => d.Id == id).Select(d => d.Secret).SingleAsync()));
    }

    [Fact]
    public async Task A_reclaimed_delivery_past_its_fourth_attempt_fails_without_calling_brevo()
    {
        await SeedDeliveryAsync(ActorId, address: null, attempts: 4);

        var delivery = await AwaitDeliveryAsync(DeliveryStatus.Failed);
        Assert.Empty(Brevo.Sends);
        Assert.Null(delivery.Secret);
        var failed = Assert.Single(await AwaitLogsAsync("DeliveryFailed", 1));
        Assert.Equal("retriesExhausted", failed.GetStructuredStateValue("reason"));
    }

    private async Task<Guid> SeedDeliveryAsync(Guid? userId, string? address, int attempts = 0)
    {
        var now = Time.GetUtcNow().UtcDateTime;
        var delivery = new Delivery
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            Type = MessageType.PasswordChanged,
            Channel = MessageChannel.Email,
            Message = MessageBodies.Of(PasswordNotice()),
            UserId = userId,
            Address = address,
            Secret = Secret,
            Status = DeliveryStatus.Pending,
            Attempts = attempts,
            IdempotencyKey = Guid.NewGuid(),
            CreatedAt = now,
            NextAttemptAt = now,
            SendBy = now.AddHours(24)
        };
        await FromDatabaseAsync(database =>
        {
            database.Deliveries.Add(delivery);
            return database.SaveChangesAsync();
        });

        return delivery.Id;
    }

    private MetricCollector<long> DeliveryMeter() =>
        new(Services.GetRequiredService<IMeterFactory>(), MessageMeter.Name, MessageMeter.DeliveriesName);
}
