using System.Diagnostics.Metrics;

namespace Laneway.Api;

public enum DeliveryResult
{
    Sent,
    Failed,
    Expired,
    Dropped
}

public sealed class MessageMeter
{
    public const string Name = "Laneway.Message";

    public const string DeliveriesName = "laneway.message.deliveries";

    public const string SuppressionsName = "laneway.message.suppressions";

    private readonly Counter<long> _deliveries;

    private readonly Counter<long> _suppressions;

    public MessageMeter(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Meter = factory.Create(Name);
        _deliveries = Meter.CreateCounter<long>(DeliveriesName);
        _suppressions = Meter.CreateCounter<long>(SuppressionsName);
    }

    public Meter Meter { get; }

    public void Delivered(DeliveryResult result, MessageChannel channel) =>
        _deliveries.Add(
            1,
            new KeyValuePair<string, object?>("result", result switch
            {
                DeliveryResult.Sent => "sent",
                DeliveryResult.Failed => "failed",
                DeliveryResult.Expired => "expired",
                DeliveryResult.Dropped => "dropped",
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
            }),
            new KeyValuePair<string, object?>("channel", channel switch
            {
                MessageChannel.Email => "email",
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
            }));

    public void Suppressed(SuppressionKind kind) =>
        _suppressions.Add(1, new KeyValuePair<string, object?>("event", Suppressions.Name(kind)));
}
