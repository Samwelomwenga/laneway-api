using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Laneway.Api.Tests;

[Collection(ApiCollection.Name)]
public class DeliveryRetryTests : ApiTests
{
    public DeliveryRetryTests(TestStack stack) : base(stack)
    {
    }

    [Fact]
    public async Task A_brevo_hiccup_retries_with_the_same_key_until_it_sends()
    {
        Brevo.Answer(HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, BrevoAnswer.Timeout);

        await AddMessageAsync(PasswordNotice());
        foreach (var (attempt, wait) in new[] { (1, 30), (2, 120), (3, 600) })
        {
            var retrying = (await AwaitLogsAsync("DeliveryRetrying", attempt))[^1];
            Assert.Equal(
                Time.GetUtcNow().UtcDateTime.AddSeconds(wait),
                DateTime.Parse(
                    retrying.GetStructuredStateValue("nextAttemptAt")!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                TimeSpan.FromSeconds(1));
            AdvanceDeliveries(TimeSpan.FromSeconds(wait));
        }

        await AwaitDeliveryAsync(DeliveryStatus.Sent);
        Assert.Equal(4, Brevo.Sends.Count);
        Assert.Single(Brevo.Sends.Select(send => send.IdempotencyKey).Distinct());
        Assert.Equal(
            ["rateLimited", "serverError", "timeout"],
            Logs().Where(log => log.Id.Name == "DeliveryRetrying").Select(log => log.GetStructuredStateValue("reason")));
        Assert.Equal(
            ["429", "500", null],
            Logs().Where(log => log.Id.Name == "DeliveryRetrying").Select(log => log.GetStructuredStateValue("status")));
    }

    [Fact]
    public async Task The_fourth_failed_attempt_fails_the_delivery()
    {
        using var deliveries = DeliveryMeter();
        Brevo.Answer(
            HttpStatusCode.ServiceUnavailable,
            BrevoAnswer.NetworkError,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable);

        await AddMessageAsync(PasswordNotice());
        foreach (var (attempt, wait) in new[] { (1, 30), (2, 120), (3, 600) })
        {
            await AwaitLogsAsync("DeliveryRetrying", attempt);
            AdvanceDeliveries(TimeSpan.FromSeconds(wait));
        }

        await AwaitDeliveryAsync(DeliveryStatus.Failed);
        Assert.Equal(4, Brevo.Sends.Count);
        var failed = Assert.Single(Logs(), log => log.Id.Name == "DeliveryFailed");
        Assert.Equal(MessageLog.Category, failed.Category);
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal("retriesExhausted", failed.GetStructuredStateValue("reason"));
        Assert.Equal("503", failed.GetStructuredStateValue("status"));
        Assert.Equal(["failed"], deliveries.GetMeasurementSnapshot().Select(m => m.Tags["result"]));
        Assert.Equal(["email"], deliveries.GetMeasurementSnapshot().Select(m => m.Tags["channel"]));
    }

    [Fact]
    public async Task A_400_fails_at_once_without_logging_brevos_message()
    {
        Brevo.Answer(new BrevoAnswer(HttpStatusCode.BadRequest, "invalid_parameter"));

        await AddMessageAsync(PasswordNotice());

        await AwaitDeliveryAsync(DeliveryStatus.Failed);
        Assert.Single(Brevo.Sends);
        var failed = Assert.Single(await AwaitLogsAsync("DeliveryFailed", 1));
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal("rejected", failed.GetStructuredStateValue("reason"));
        Assert.Equal("400", failed.GetStructuredStateValue("status"));
        Assert.Equal("invalid_parameter", failed.GetStructuredStateValue("code"));
        Assert.DoesNotContain(Logs(), log => log.Message.Contains("refused", StringComparison.Ordinal));
        Assert.DoesNotContain(Logs(), log => log.Id.Name == "DeliveryRetrying");
    }

    [Fact]
    public async Task An_unknown_brevo_code_is_not_logged()
    {
        Brevo.Answer(new BrevoAnswer(HttpStatusCode.BadRequest, "someone@example.com"));

        await AddMessageAsync(PasswordNotice());

        var failed = Assert.Single(await AwaitLogsAsync("DeliveryFailed", 1));
        Assert.Null(failed.GetStructuredStateValue("code"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData(HttpStatusCode.PaymentRequired, "not_enough_credits")]
    [InlineData(HttpStatusCode.Forbidden, "permission_denied")]
    public async Task A_broken_key_or_account_fails_at_error(HttpStatusCode status, string code)
    {
        using var deliveries = DeliveryMeter();
        Brevo.Answer(new BrevoAnswer(status, code));

        await AddMessageAsync(PasswordNotice());

        await AwaitDeliveryAsync(DeliveryStatus.Failed);
        var failed = Assert.Single(await AwaitLogsAsync("DeliveryFailed", 1));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal("rejected", failed.GetStructuredStateValue("reason"));
        Assert.Equal(code, failed.GetStructuredStateValue("code"));
        Assert.Equal(["failed"], deliveries.GetMeasurementSnapshot().Select(m => m.Tags["result"]));
    }

    [Fact]
    public async Task A_sent_delivery_counts_on_the_meter()
    {
        using var deliveries = DeliveryMeter();

        await AddMessageAsync(PasswordNotice());

        await AwaitDeliveryAsync(DeliveryStatus.Sent);
        await AwaitLogsAsync("DeliverySent", 1);
        Assert.Equal(["sent"], deliveries.GetMeasurementSnapshot().Select(m => m.Tags["result"]));
    }

    [Fact]
    public async Task Up_to_four_sends_run_at_once()
    {
        Brevo.Hold();
        for (var n = 0; n < 6; n++)
        {
            await AddMessageAsync(PasswordNotice());
        }

        await Brevo.AwaitInFlightAsync(4);
        Brevo.Release();

        await Brevo.AwaitSendsAsync(6);
        Assert.Equal(4, Brevo.MaxInFlight);
    }

    private MetricCollector<long> DeliveryMeter() =>
        new(Services.GetRequiredService<IMeterFactory>(), MessageMeter.Name, MessageMeter.DeliveriesName);
}
