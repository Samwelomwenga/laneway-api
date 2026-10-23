namespace Laneway.Api;

public static class MessageLog
{
    public const string Category = "Laneway.Message";

    private static readonly EventId DeliverySentId = new(1, "DeliverySent");

    private static readonly EventId DeliveryRetryingId = new(2, "DeliveryRetrying");

    private static readonly EventId DeliveryFailedId = new(3, "DeliveryFailed");

    private static readonly EventId DeliveryExpiredId = new(4, "DeliveryExpired");

    private static readonly EventId DeliveryDroppedId = new(5, "DeliveryDropped");

    private const string DeliveryFailedMessage = "Delivery failed: reason {reason}, status {status}, code {code}";

    private static readonly Action<ILogger, string, Exception?> DeliverySentEvent =
        LoggerMessage.Define<string>(
            LogLevel.Information, DeliverySentId, "Delivery sent: providerMessageId {providerMessageId}");

    private static readonly Action<ILogger, string, int?, DateTime, Exception?> DeliveryRetryingEvent =
        LoggerMessage.Define<string, int?, DateTime>(
            LogLevel.Information,
            DeliveryRetryingId,
            "Delivery retrying: reason {reason}, status {status}, nextAttemptAt {nextAttemptAt}");

    private static readonly Action<ILogger, string, int?, string?, Exception?> DeliveryFailedEvent =
        LoggerMessage.Define<string, int?, string?>(LogLevel.Warning, DeliveryFailedId, DeliveryFailedMessage);

    private static readonly Action<ILogger, string, int?, string?, Exception?> DeliveryFailedErrorEvent =
        LoggerMessage.Define<string, int?, string?>(LogLevel.Error, DeliveryFailedId, DeliveryFailedMessage);

    private static readonly Action<ILogger, DateTime, Exception?> DeliveryExpiredEvent =
        LoggerMessage.Define<DateTime>(LogLevel.Warning, DeliveryExpiredId, "Delivery expired: sendBy {sendBy}");

    private static readonly Action<ILogger, string, Exception?> DeliveryDroppedEvent =
        LoggerMessage.Define<string>(LogLevel.Information, DeliveryDroppedId, "Delivery dropped: reason {reason}");

    public static void DeliverySent(this ILogger logger, string providerMessageId) =>
        DeliverySentEvent(logger, providerMessageId, null);

    public static void DeliveryRetrying(this ILogger logger, string reason, int? status, DateTime nextAttemptAt) =>
        DeliveryRetryingEvent(logger, reason, status, nextAttemptAt, null);

    public static void DeliveryFailed(this ILogger logger, string reason, int? status, string? code)
    {
        var failed = status is 401 or 402 or 403 ? DeliveryFailedErrorEvent : DeliveryFailedEvent;
        failed(logger, reason, status, code, null);
    }

    public static void DeliveryExpired(this ILogger logger, DateTime sendBy) => DeliveryExpiredEvent(logger, sendBy, null);

    public static void DeliveryDropped(this ILogger logger, string reason) => DeliveryDroppedEvent(logger, reason, null);

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
