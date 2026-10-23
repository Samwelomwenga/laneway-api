namespace Laneway.Api;

public static class MessageLog
{
    public const string Category = "Laneway.Message";

    private static readonly EventId DeliverySentId = new(1, "DeliverySent");

    private static readonly EventId DeliveryRetryingId = new(2, "DeliveryRetrying");

    private static readonly EventId DeliveryFailedId = new(3, "DeliveryFailed");

    private static readonly EventId DeliveryExpiredId = new(4, "DeliveryExpired");

    private static readonly EventId DeliveryDroppedId = new(5, "DeliveryDropped");

    private static readonly EventId WebhookRejectedId = new(6, "WebhookRejected");

    private static readonly EventId AddressSuppressedId = new(7, "AddressSuppressed");

    private static readonly EventId WebhookEventUnmatchedId = new(8, "WebhookEventUnmatched");

    private static readonly EventId WebhookEventIgnoredId = new(9, "WebhookEventIgnored");

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

    private static readonly Action<ILogger, string, string, Exception?> DeliverySuppressedEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Information, DeliveryDroppedId, "Delivery dropped: reason {reason}, suppressedBy {suppressedBy}");

    private static readonly Action<ILogger, string, Exception?> WebhookRejectedEvent =
        LoggerMessage.Define<string>(LogLevel.Warning, WebhookRejectedId, "Webhook rejected: reason {reason}");

    private static readonly Action<ILogger, string, Guid, string, bool, Exception?> AddressSuppressedEvent =
        LoggerMessage.Define<string, Guid, string, bool>(
            LogLevel.Information,
            AddressSuppressedId,
            "Address suppressed: event {event}, deliveryId {deliveryId}, messageType {messageType}, alreadySuppressed {alreadySuppressed}");

    private static readonly Action<ILogger, string, string, Exception?> WebhookEventUnmatchedEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, WebhookEventUnmatchedId, "Webhook event unmatched: event {event}, reason {reason}");

    private static readonly Action<ILogger, string, Exception?> WebhookEventIgnoredEvent =
        LoggerMessage.Define<string>(LogLevel.Information, WebhookEventIgnoredId, "Webhook event ignored: event {event}");

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

    public static void DeliverySuppressed(this ILogger logger, SuppressionKind by) =>
        DeliverySuppressedEvent(logger, "suppressed", Suppressions.Name(by), null);

    public static void WebhookRejected(this ILogger logger, string reason) => WebhookRejectedEvent(logger, reason, null);

    public static void AddressSuppressed(
        this ILogger logger, string brevoEvent, Guid deliveryId, MessageType type, bool alreadySuppressed) =>
        AddressSuppressedEvent(logger, brevoEvent, deliveryId, type.ToString(), alreadySuppressed, null);

    public static void WebhookEventUnmatched(this ILogger logger, string brevoEvent, string reason) =>
        WebhookEventUnmatchedEvent(logger, brevoEvent, reason, null);

    public static void WebhookEventIgnored(this ILogger logger, string brevoEvent) =>
        WebhookEventIgnoredEvent(logger, brevoEvent, null);

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

public static class WebhookRejection
{
    public const string Missing = "missing";
    public const string Malformed = "malformed";
    public const string Expired = "expired";
    public const string NotYetValid = "notYetValid";
    public const string InvalidSignature = "invalidSignature";
    public const string InvalidAlgorithm = "invalidAlgorithm";
    public const string InvalidAudience = "invalidAudience";
}
