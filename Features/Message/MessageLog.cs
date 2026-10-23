namespace Laneway.Api;

public static class MessageLog
{
    public const string Category = "Laneway.Message";

    private static readonly EventId DeliverySentId = new(1, "DeliverySent");

    private static readonly Action<ILogger, string, Exception?> DeliverySentEvent =
        LoggerMessage.Define<string>(
            LogLevel.Information, DeliverySentId, "Delivery sent: providerMessageId {providerMessageId}");

    public static void DeliverySent(this ILogger logger, string providerMessageId) =>
        DeliverySentEvent(logger, providerMessageId, null);

    public static IDisposable? BeginDeliveryScope(this ILogger logger, Delivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var scope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["deliveryId"] = delivery.Id,
            ["messageType"] = delivery.Type.ToString(),
            ["channel"] = delivery.Channel.ToString(),
            ["attempt"] = delivery.Attempts
        };
        if (delivery.UserId is { } sub)
        {
            scope["sub"] = sub;
        }

        return logger.BeginScope(scope);
    }
}
