using System.Linq.Expressions;

namespace Laneway.Api;

public class Session
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromDays(7);

    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public SessionEndReason? EndReason { get; set; }
    public required string UserAgent { get; set; }

    public static Expression<Func<Session, bool>> ActiveAt(DateTime now)
    {
        var idleSince = now - IdleTimeout;
        return session => session.EndedAt == null && session.ExpiresAt > now && session.LastUsedAt > idleSince;
    }
}

public enum SessionEndReason
{
    SignedOut,
    SignedOutEverywhere,
    EndedFromSessionList,
    PasswordChanged,
    UserDeleted,
    TokenReused
}
