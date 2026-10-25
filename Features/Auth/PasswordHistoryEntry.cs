namespace Laneway.Api;

public class PasswordHistoryEntry
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);

    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(14);

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime ReplacedAt { get; set; }

    public static PasswordHistoryEntry Of(Guid userId, string passwordHash, DateTime replacedAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        PasswordHash = passwordHash,
        ReplacedAt = replacedAt
    };
}
