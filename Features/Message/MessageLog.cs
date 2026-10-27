using System.Diagnostics.CodeAnalysis;

namespace Laneway.Api;

[SuppressMessage("Usage", "SYSLIB1025", Justification = "Variants of one event share its name.")]
public static partial class MessageLog
{
    public const string Category = "Laneway.Message";

    private const string DeliveryFailedMessage = "Delivery failed: reason {reason}, status {status}, code {code}";

    [LoggerMessage(EventId = 1, EventName = "DeliverySent", Level = LogLevel.Information,
        Message = "Delivery sent: providerMessageId {providerMessageId}")]
    public static partial void DeliverySent(this ILogger logger, string providerMessageId);

    [LoggerMessage(EventId = 2, EventName = "DeliveryRetrying", Level = LogLevel.Information,
        Message = "Delivery retrying: reason {reason}, status {status}, nextAttemptAt {nextAttemptAt}")]
    public static partial void DeliveryRetrying(this ILogger logger, string reason, int? status, DateTime nextAttemptAt);

    [LoggerMessage(EventId = 3, EventName = "DeliveryFailed", Level = LogLevel.Warning, Message = DeliveryFailedMessage)]
    private static partial void DeliveryFailedWarning(ILogger logger, string reason, int? status, string? code);

    [LoggerMessage(EventId = 3, EventName = "DeliveryFailed", Level = LogLevel.Error, Message = DeliveryFailedMessage)]
    private static partial void DeliveryFailedError(ILogger logger, string reason, int? status, string? code);

    [LoggerMessage(EventId = 4, EventName = "DeliveryExpired", Level = LogLevel.Warning, Message = "Delivery expired: sendBy {sendBy}")]
    public static partial void DeliveryExpired(this ILogger logger, DateTime sendBy);

    [LoggerMessage(EventId = 5, EventName = "DeliveryDropped", Level = LogLevel.Information,
        Message = "Delivery dropped: reason {reason}")]
    public static partial void DeliveryDropped(this ILogger logger, string reason);

    [LoggerMessage(EventId = 5, EventName = "DeliveryDropped", Level = LogLevel.Information,
        Message = "Delivery dropped: reason {reason}, suppressedBy {suppressedBy}")]
    private static partial void DeliverySuppressedEvent(ILogger logger, string reason, string suppressedBy);

    [LoggerMessage(EventId = 6, EventName = "WebhookRejected", Level = LogLevel.Warning, Message = "Webhook rejected: reason {reason}")]
    private static partial void WebhookRejectedWithoutIp(ILogger logger, string reason);

    [LoggerMessage(EventId = 6, EventName = "WebhookRejected", Level = LogLevel.Warning,
        Message = "Webhook rejected: reason {reason}, clientIp {clientIp}")]
    private static partial void WebhookRejectedWithIp(ILogger logger, string reason, string clientIp);

    [LoggerMessage(EventId = 7, EventName = "AddressSuppressed", Level = LogLevel.Information,
        Message = "Address suppressed: event {event}, deliveryId {deliveryId}, messageType {messageType}, alreadySuppressed {alreadySuppressed}")]
    private static partial void AddressSuppressedEvent(
        ILogger logger, string @event, Guid deliveryId, string messageType, bool alreadySuppressed);

    [LoggerMessage(EventId = 8, EventName = "WebhookEventUnmatched", Level = LogLevel.Warning,
        Message = "Webhook event unmatched: event {event}, reason {reason}")]
    public static partial void WebhookEventUnmatched(this ILogger logger, string @event, string reason);

    [LoggerMessage(EventId = 9, EventName = "WebhookEventIgnored", Level = LogLevel.Information,
        Message = "Webhook event ignored: event {event}")]
    public static partial void WebhookEventIgnored(this ILogger logger, string @event);

    [LoggerMessage(EventId = 10, EventName = "DeliveryPollFailed", Level = LogLevel.Warning,
        Message = "A delivery poll failed. The next poll retries it.")]
    public static partial void DeliveryPollFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 11, EventName = "DeliverySendThrew", Level = LogLevel.Error,
        Message = "A delivery send threw. The claim's lease runs out and the next poll retries it.")]
    public static partial void DeliverySendThrew(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 12, EventName = "InvitesUnsubscribed", Level = LogLevel.Information,
        Message = "Invites unsubscribed: alreadySuppressed {alreadySuppressed}")]
    public static partial void InvitesUnsubscribed(this ILogger logger, bool alreadySuppressed);

    public static void DeliveryFailed(this ILogger logger, string reason, int? status, string? code)
    {
        if (status is 401 or 402 or 403)
        {
            DeliveryFailedError(logger, reason, status, code);
        }
        else
        {
            DeliveryFailedWarning(logger, reason, status, code);
        }
    }

    public static void WebhookRejected(this ILogger logger, string reason, string? clientIp)
    {
        if (clientIp is null)
        {
            WebhookRejectedWithoutIp(logger, reason);
        }
        else
        {
            WebhookRejectedWithIp(logger, reason, clientIp);
        }
    }

    public static void DeliverySuppressed(this ILogger logger, SuppressionKind by) =>
        DeliverySuppressedEvent(logger, "suppressed", Suppressions.Name(by));

    public static void AddressSuppressed(
        this ILogger logger, string brevoEvent, Guid deliveryId, MessageType type, bool alreadySuppressed) =>
        AddressSuppressedEvent(logger, brevoEvent, deliveryId, type.ToString(), alreadySuppressed);

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
