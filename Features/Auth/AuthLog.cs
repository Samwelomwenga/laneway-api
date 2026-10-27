using System.Diagnostics.CodeAnalysis;

namespace Laneway.Api;

[SuppressMessage("Usage", "SYSLIB1025", Justification = "Variants of one event share its name.")]
public static partial class AuthLog
{
    public const string Category = "Laneway.Auth";

    [LoggerMessage(EventId = 1, EventName = "AccessTokenRejected", Level = LogLevel.Information,
        Message = "Access token rejected: {reason}, kid {kid}")]
    private static partial void RejectedWithKid(ILogger logger, string reason, string kid);

    [LoggerMessage(EventId = 1, EventName = "AccessTokenRejected", Level = LogLevel.Information,
        Message = "Access token rejected: {reason}, kidKnown {kidKnown}")]
    private static partial void RejectedWithoutKid(ILogger logger, string reason, bool kidKnown);

    [LoggerMessage(EventId = 2, EventName = "SignUp", Level = LogLevel.Information, Message = "Signed up: sub {sub}")]
    public static partial void SignUp(this ILogger logger, Guid sub);

    [LoggerMessage(EventId = 3, EventName = "SignInSucceeded", Level = LogLevel.Information,
        Message = "Signed in: sub {sub}, sid {sid}")]
    public static partial void SignInSucceeded(this ILogger logger, Guid sub, Guid sid);

    [LoggerMessage(EventId = 4, EventName = "SignInFailed", Level = LogLevel.Warning,
        Message = "Sign-in failed: sub {sub}, blocked {blocked}")]
    private static partial void SignInFailedWithSub(ILogger logger, Guid sub, bool blocked);

    [LoggerMessage(EventId = 4, EventName = "SignInFailed", Level = LogLevel.Warning,
        Message = "Sign-in failed: accountFound {accountFound}, blocked {blocked}")]
    private static partial void SignInFailedWithoutAccount(ILogger logger, bool accountFound, bool blocked);

    [LoggerMessage(EventId = 5, EventName = "BreachedCheckUnavailable", Level = LogLevel.Warning,
        Message = "Breached password check unavailable, password accepted")]
    public static partial void BreachedCheckUnavailable(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, EventName = "SessionRefreshed", Level = LogLevel.Information,
        Message = "Session refreshed: sub {sub}, sid {sid}, refreshTokenId {refreshTokenId}")]
    public static partial void SessionRefreshed(this ILogger logger, Guid sub, Guid sid, Guid refreshTokenId);

    [LoggerMessage(EventId = 7, EventName = "RefreshSuperseded", Level = LogLevel.Information,
        Message = "Refresh superseded: sub {sub}, sid {sid}")]
    public static partial void RefreshSuperseded(this ILogger logger, Guid sub, Guid sid);

    [LoggerMessage(EventId = 8, EventName = "RefreshTokenReused", Level = LogLevel.Warning,
        Message = "Refresh token reused: sub {sub}, sid {sid}, refreshTokenId {refreshTokenId}")]
    public static partial void RefreshTokenReused(this ILogger logger, Guid sub, Guid sid, Guid refreshTokenId);

    [LoggerMessage(EventId = 9, EventName = "RefreshRejected", Level = LogLevel.Information,
        Message = "Refresh rejected: {reason}, sid {sid}")]
    private static partial void RefreshRejectedWithSid(ILogger logger, string reason, Guid sid);

    [LoggerMessage(EventId = 9, EventName = "RefreshRejected", Level = LogLevel.Information,
        Message = "Refresh rejected: {reason}")]
    private static partial void RefreshRejectedWithoutSid(ILogger logger, string reason);

    [LoggerMessage(EventId = 10, EventName = "SessionEnded", Level = LogLevel.Information,
        Message = "Session ended: sub {sub}, sid {sid}, endReason {endReason}")]
    public static partial void SessionEnded(this ILogger logger, Guid sub, Guid sid, SessionEndReason endReason);

    [LoggerMessage(EventId = 11, EventName = "CsrfRejected", Level = LogLevel.Warning, Message = "CSRF check failed: {check}")]
    private static partial void CsrfRejectedEvent(ILogger logger, string check);

    [LoggerMessage(EventId = 12, EventName = "SignInBlockStarted", Level = LogLevel.Warning,
        Message = "Sign-in block started: sub {sub}, failureCount {failureCount}, blockedUntil {blockedUntil}")]
    public static partial void SignInBlockStarted(this ILogger logger, Guid sub, int failureCount, DateTime blockedUntil);

    [LoggerMessage(EventId = 13, EventName = "PasswordCheckFailed", Level = LogLevel.Warning,
        Message = "Password check failed: sub {sub}, sid {sid}, blocked {blocked}")]
    public static partial void PasswordCheckFailed(this ILogger logger, Guid sub, Guid sid, bool blocked);

    [LoggerMessage(EventId = 13, EventName = "PasswordCheckFailed", Level = LogLevel.Warning,
        Message = "Password check failed: sub {sub}, blocked {blocked}")]
    public static partial void PasswordCheckFailed(this ILogger logger, Guid sub, bool blocked);

    [LoggerMessage(EventId = 14, EventName = "PasswordChanged", Level = LogLevel.Information,
        Message = "Password changed: sub {sub}, sid {sid}")]
    public static partial void PasswordChanged(this ILogger logger, Guid sub, Guid sid);

    [LoggerMessage(EventId = 14, EventName = "PasswordChanged", Level = LogLevel.Information,
        Message = "Password reset: sub {sub}")]
    public static partial void PasswordReset(this ILogger logger, Guid sub);

    [LoggerMessage(EventId = 15, EventName = "EmailChanged", Level = LogLevel.Information, Message = "Email changed: sub {sub}")]
    public static partial void EmailChanged(this ILogger logger, Guid sub);

    [LoggerMessage(EventId = 16, EventName = "UserDeleted", Level = LogLevel.Information,
        Message = "User deleted: sub {sub}, sid {sid}")]
    public static partial void UserDeleted(this ILogger logger, Guid sub, Guid sid);

    [LoggerMessage(EventId = 17, EventName = "RateLimited", Level = LogLevel.Warning,
        Message = "Rate limited: policy {policy}, sub {sub}")]
    private static partial void RateLimitedWithSub(ILogger logger, string policy, Guid sub);

    [LoggerMessage(EventId = 17, EventName = "RateLimited", Level = LogLevel.Warning, Message = "Rate limited: policy {policy}")]
    private static partial void RateLimitedWithoutSub(ILogger logger, string policy);

    [LoggerMessage(EventId = 18, EventName = "MessageSkipped", Level = LogLevel.Information,
        Message = "Message skipped: {messageType}, {reason}, sub {sub}")]
    private static partial void MessageSkippedWithSub(ILogger logger, MessageType messageType, string reason, Guid sub);

    [LoggerMessage(EventId = 18, EventName = "MessageSkipped", Level = LogLevel.Information,
        Message = "Message skipped: {messageType}, {reason}")]
    private static partial void MessageSkippedWithoutSub(ILogger logger, MessageType messageType, string reason);

    [LoggerMessage(EventId = 18, EventName = "MessageSkipped", Level = LogLevel.Warning,
        Message = "Message skipped: {messageType}, {reason}, sub {sub}")]
    private static partial void MessageSkippedAsWarning(ILogger logger, MessageType messageType, string reason, Guid sub);

    [LoggerMessage(EventId = 19, EventName = "EmailChangeUndone", Level = LogLevel.Warning,
        Message = "Email change undone: sub {sub}")]
    public static partial void EmailChangeUndone(this ILogger logger, Guid sub);

    [LoggerMessage(EventId = 20, EventName = "UsernameChanged", Level = LogLevel.Information,
        Message = "Username changed: sub {sub}")]
    public static partial void UsernameChanged(this ILogger logger, Guid sub);

    [LoggerMessage(EventId = 21, EventName = "SigningKeysLoaded", Level = LogLevel.Information,
        Message = "Signing keys loaded: kids {kids}, signingKid {signingKid}")]
    private static partial void SigningKeysLoadedEvent(ILogger logger, string[] kids, string signingKid);

    public static void SigningKeysLoaded(this ILogger logger, SigningKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        SigningKeysLoadedEvent(logger, [.. keys.All.Select(key => key.KeyId)], keys.Signing.Key.KeyId);
    }

    public static void SignInFailed(this ILogger logger, Guid? userId, bool blocked)
    {
        if (userId is { } sub)
        {
            SignInFailedWithSub(logger, sub, blocked);
        }
        else
        {
            SignInFailedWithoutAccount(logger, false, blocked);
        }
    }

    public static void RefreshRejected(this ILogger logger, string reason, Guid? sessionId)
    {
        if (sessionId is { } sid)
        {
            RefreshRejectedWithSid(logger, reason, sid);
        }
        else
        {
            RefreshRejectedWithoutSid(logger, reason);
        }
    }

    public static void CsrfRejected(this ILogger logger, FailedCsrfCheck check) =>
        CsrfRejectedEvent(logger, check switch
        {
            FailedCsrfCheck.Header => "header",
            FailedCsrfCheck.Origin => "origin",
            FailedCsrfCheck.FetchSite => "fetch-site",
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, null)
        });

    public static void RateLimited(this ILogger logger, string policy, Guid? userId)
    {
        if (userId is { } sub)
        {
            RateLimitedWithSub(logger, policy, sub);
        }
        else
        {
            RateLimitedWithoutSub(logger, policy);
        }
    }

    public static void MessageSkipped(this ILogger logger, MessageType type, string reason, Guid? userId)
    {
        if (userId is not { } sub)
        {
            MessageSkippedWithoutSub(logger, type, reason);
        }
        else if (reason == MessageSkip.AddressCapped)
        {
            MessageSkippedAsWarning(logger, type, reason, sub);
        }
        else
        {
            MessageSkippedWithSub(logger, type, reason, sub);
        }
    }

    public static void AccessTokenRejected(this ILogger logger, string reason, string? knownKid)
    {
        if (knownKid is null)
        {
            RejectedWithoutKid(logger, reason, false);
        }
        else
        {
            RejectedWithKid(logger, reason, knownKid);
        }
    }
}

public static class AccessTokenRejection
{
    public const string Malformed = "malformed";
    public const string Expired = "expired";
    public const string UnknownKey = "unknownKey";
    public const string InvalidSignature = "invalidSignature";
    public const string InvalidAlgorithm = "invalidAlgorithm";
    public const string InvalidType = "invalidType";
    public const string InvalidIssuer = "invalidIssuer";
    public const string InvalidAudience = "invalidAudience";
    public const string SessionEnded = "sessionEnded";
    public const string Invalid = "invalid";
}

public static class MessageSkip
{
    public const string UnknownAccount = "unknownAccount";
    public const string Verified = "verified";
    public const string Unverified = "unverified";
    public const string Suppressed = "suppressed";
    public const string AddressCapped = "addressCapped";
}

public static class RefreshRejection
{
    public const string MissingCookie = "missingCookie";
    public const string UnknownToken = "unknownToken";
    public const string SessionEnded = "sessionEnded";
}
