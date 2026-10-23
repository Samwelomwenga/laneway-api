using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public enum SuppressionKind
{
    HardBounce,
    Spam,
    Blocked,
    Unsubscribed
}

public class Suppression
{
    public required byte[] AddressHash { get; set; }
    public SuppressionKind Kind { get; set; }
    public DateTime SuppressedAt { get; set; }
    public required string MessageId { get; set; }
}

public static class Suppressions
{
    public static byte[] Hash(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return SHA256.HashData(Encoding.UTF8.GetBytes(Emails.Normalize(address)));
    }

    /// <summary>
    /// The suppression that stops a message on this route to this address, or null when none does. An unsubscribe
    /// comes first, since the worker checks opt-outs before the other suppressions.
    /// </summary>
    public static async Task<SuppressionKind?> StoppingAsync(
        ApplicationDbContext database, string address, MessageRoute route, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(database);

        var hash = Hash(address);
        var kinds = await database.Suppressions.Where(s => s.AddressHash == hash).Select(s => s.Kind).ToListAsync(token);
        var stopping = kinds.Where(kind => Stops(kind, route)).ToList();
        return stopping.Contains(SuppressionKind.Unsubscribed) ? SuppressionKind.Unsubscribed
            : stopping.Count > 0 ? stopping.Min()
            : null;
    }

    /// <summary>
    /// Whether a suppression of this kind stops a message on this route. An unsubscribe stops only opt-out messages.
    /// </summary>
    public static bool Stops(SuppressionKind kind, MessageRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return kind != SuppressionKind.Unsubscribed || route.OptOut;
    }

    public static string Name(SuppressionKind kind) => kind switch
    {
        SuppressionKind.HardBounce => "hardBounce",
        SuppressionKind.Spam => "spam",
        SuppressionKind.Blocked => "blocked",
        SuppressionKind.Unsubscribed => "unsubscribed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
