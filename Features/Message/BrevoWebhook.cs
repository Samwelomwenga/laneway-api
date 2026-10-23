using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Laneway.Api;

public sealed class BrevoWebhook(
    ApplicationDbContext database, TimeProvider time, MessageMeter meter, ILoggerFactory loggers)
{
    public const string Other = "other";

    private static readonly Dictionary<string, string> Ignored = new(StringComparer.Ordinal)
    {
        ["soft_bounce"] = "softBounce",
        ["delivered"] = "delivered",
        ["deferred"] = "deferred",
        ["request"] = "request",
        ["invalid_email"] = "invalidEmail",
        ["opened"] = "opened",
        ["unique_opened"] = "uniqueOpened",
        ["click"] = "click",
        ["proxy_open"] = "proxyOpen",
        ["unique_proxy_open"] = "uniqueProxyOpen"
    };

    private static readonly Dictionary<string, SuppressionKind> Handled = new(StringComparer.Ordinal)
    {
        ["hard_bounce"] = SuppressionKind.HardBounce,
        ["spam"] = SuppressionKind.Spam,
        ["blocked"] = SuppressionKind.Blocked,
        ["unsubscribed"] = SuppressionKind.Unsubscribed
    };

    private readonly ILogger _log = loggers.CreateLogger(MessageLog.Category);

    public async Task ReceiveAsync(JsonElement payload, CancellationToken token)
    {
        var brevoEvent = Text(payload, "event") ?? string.Empty;
        if (!Handled.TryGetValue(brevoEvent, out var kind))
        {
            _log.WebhookEventIgnored(Ignored.GetValueOrDefault(brevoEvent, Other));
            return;
        }

        var name = Suppressions.Name(kind);

        if (Text(payload, "message-id") is not { Length: > 0 } messageId)
        {
            _log.WebhookEventUnmatched(name, "missingMessageId");
            return;
        }

        var delivery = await database.Deliveries
            .AsNoTracking()
            .Where(d => d.ProviderMessageId == messageId)
            .Select(d => new { d.Id, d.Type, d.Address })
            .FirstOrDefaultAsync(token);
        if (delivery is null)
        {
            _log.WebhookEventUnmatched(name, "unknownMessageId");
            return;
        }

        var email = Emails.Normalize(Text(payload, "email"));
        if (email is null || email != Emails.Normalize(delivery.Address))
        {
            _log.WebhookEventUnmatched(name, "addressMismatch");
            return;
        }

        var written = await SuppressAsync(Suppressions.Hash(email), kind, messageId, token);
        _log.AddressSuppressed(name, delivery.Id, delivery.Type, alreadySuppressed: !written);
        if (written)
        {
            meter.Suppressed(kind);
        }
    }

    private async Task<bool> SuppressAsync(byte[] hash, SuppressionKind kind, string messageId, CancellationToken token)
    {
        if (await database.Suppressions.AnyAsync(s => s.AddressHash == hash && s.Kind == kind, token))
        {
            return false;
        }

        database.Suppressions.Add(new Suppression
        {
            AddressHash = hash,
            Kind = kind,
            SuppressedAt = time.GetUtcNow().UtcDateTime,
            MessageId = messageId
        });
        try
        {
            await database.SaveChangesAsync(token);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    private static string? Text(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
