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

    public static Session Start(Guid userId, string? userAgent, DateTime now)
    {
        var agent = userAgent ?? string.Empty;
        if (agent.Length > FieldLimits.SessionUserAgent)
        {
            var end = char.IsHighSurrogate(agent[FieldLimits.SessionUserAgent - 1])
                ? FieldLimits.SessionUserAgent - 1
                : FieldLimits.SessionUserAgent;
            agent = agent[..end];
        }

        return new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now + AbsoluteLifetime,
            UserAgent = agent
        };
    }

    public static Expression<Func<Session, bool>> ActiveAt(DateTime now)
    {
        var idleSince = now - IdleTimeout;
        return session => session.EndedAt == null && session.ExpiresAt > now && session.LastUsedAt > idleSince;
    }

    public static Expression<Func<Session, bool>> FinishedBefore(DateTime cutoff)
    {
        var idleBefore = cutoff - IdleTimeout;
        return session => session.EndedAt < cutoff || session.ExpiresAt < cutoff || session.LastUsedAt < idleBefore;
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
