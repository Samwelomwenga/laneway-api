using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public static class AddressCap
{
    public const int Limit = 5;

    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private static readonly MessageType[] Capped = [MessageType.VerifyEmail, MessageType.ResetPassword];

    public static async Task<bool> ReachedAsync(ApplicationDbContext database, Guid userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(database);

        var since = now - Window;
        return await database.Deliveries
            .CountAsync(d => d.UserId == userId && Capped.Contains(d.Type) && d.CreatedAt > since) >= Limit;
    }

    public static async Task<bool> NewAddressReachedAsync(ApplicationDbContext database, string address, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(database);

        var since = now - Window;
        return await database.Deliveries
            .CountAsync(d => d.Address == address && d.Type == MessageType.ConfirmEmailChange && d.CreatedAt > since) >= Limit;
    }
}
