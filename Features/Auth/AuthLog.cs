namespace Laneway.Api;

public static class AuthLog
{
    public const string Category = "Laneway.Auth";

    private static readonly EventId AccessTokenRejectedId = new(1, "AccessTokenRejected");

    private static readonly Action<ILogger, string, string, Exception?> RejectedWithKid =
        LoggerMessage.Define<string, string>(
            LogLevel.Information, AccessTokenRejectedId, "Access token rejected: {reason}, kid {kid}");

    private static readonly Action<ILogger, string, bool, Exception?> RejectedWithoutKid =
        LoggerMessage.Define<string, bool>(
            LogLevel.Information, AccessTokenRejectedId, "Access token rejected: {reason}, kidKnown {kidKnown}");

    private static readonly EventId SignUpId = new(2, "SignUp");

    private static readonly EventId SignInSucceededId = new(3, "SignInSucceeded");

    private static readonly EventId SignInFailedId = new(4, "SignInFailed");

    private static readonly EventId BreachedCheckUnavailableId = new(5, "BreachedCheckUnavailable");

    private static readonly Action<ILogger, Exception?> BreachedCheckUnavailableEvent =
        LoggerMessage.Define(
            LogLevel.Warning, BreachedCheckUnavailableId, "Breached password check unavailable, password accepted");

    private static readonly Action<ILogger, Guid, Exception?> SignUpEvent =
        LoggerMessage.Define<Guid>(LogLevel.Information, SignUpId, "Signed up: sub {sub}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> SignInSucceededEvent =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, SignInSucceededId, "Signed in: sub {sub}, sid {sid}");

    private static readonly Action<ILogger, Guid, bool, Exception?> SignInFailedWithSub =
        LoggerMessage.Define<Guid, bool>(
            LogLevel.Warning, SignInFailedId, "Sign-in failed: sub {sub}, blocked {blocked}");

    private static readonly Action<ILogger, bool, bool, Exception?> SignInFailedWithoutAccount =
        LoggerMessage.Define<bool, bool>(
            LogLevel.Warning, SignInFailedId, "Sign-in failed: accountFound {accountFound}, blocked {blocked}");

    private static readonly EventId SessionRefreshedId = new(6, "SessionRefreshed");

    private static readonly EventId RefreshSupersededId = new(7, "RefreshSuperseded");

    private static readonly EventId RefreshTokenReusedId = new(8, "RefreshTokenReused");

    private static readonly EventId RefreshRejectedId = new(9, "RefreshRejected");

    private static readonly EventId SessionEndedId = new(10, "SessionEnded");

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> SessionRefreshedEvent =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information, SessionRefreshedId, "Session refreshed: sub {sub}, sid {sid}, refreshTokenId {refreshTokenId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> RefreshSupersededEvent =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information, RefreshSupersededId, "Refresh superseded: sub {sub}, sid {sid}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> RefreshTokenReusedEvent =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Warning, RefreshTokenReusedId, "Refresh token reused: sub {sub}, sid {sid}, refreshTokenId {refreshTokenId}");

    private static readonly Action<ILogger, string, Guid, Exception?> RefreshRejectedWithSid =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Information, RefreshRejectedId, "Refresh rejected: {reason}, sid {sid}");

    private static readonly Action<ILogger, string, Exception?> RefreshRejectedWithoutSid =
        LoggerMessage.Define<string>(LogLevel.Information, RefreshRejectedId, "Refresh rejected: {reason}");

    private static readonly Action<ILogger, Guid, Guid, SessionEndReason, Exception?> SessionEndedEvent =
        LoggerMessage.Define<Guid, Guid, SessionEndReason>(
            LogLevel.Information, SessionEndedId, "Session ended: sub {sub}, sid {sid}, endReason {endReason}");

    private static readonly EventId CsrfRejectedId = new(11, "CsrfRejected");

    private static readonly Action<ILogger, string, Exception?> CsrfRejectedEvent =
        LoggerMessage.Define<string>(LogLevel.Warning, CsrfRejectedId, "CSRF check failed: {check}");

    private static readonly EventId SignInBlockStartedId = new(12, "SignInBlockStarted");

    private static readonly Action<ILogger, Guid, int, DateTime, Exception?> SignInBlockStartedEvent =
        LoggerMessage.Define<Guid, int, DateTime>(
            LogLevel.Warning,
            SignInBlockStartedId,
            "Sign-in block started: sub {sub}, failureCount {failureCount}, blockedUntil {blockedUntil}");

    private static readonly EventId PasswordCheckFailedId = new(13, "PasswordCheckFailed");

    private static readonly Action<ILogger, Guid, Guid, bool, Exception?> PasswordCheckFailedEvent =
        LoggerMessage.Define<Guid, Guid, bool>(
            LogLevel.Warning, PasswordCheckFailedId, "Password check failed: sub {sub}, sid {sid}, blocked {blocked}");

    private static readonly Action<ILogger, Guid, bool, Exception?> PasswordCheckFailedWithoutSid =
        LoggerMessage.Define<Guid, bool>(
            LogLevel.Warning, PasswordCheckFailedId, "Password check failed: sub {sub}, blocked {blocked}");

    private static readonly EventId PasswordChangedId = new(14, "PasswordChanged");

    private static readonly Action<ILogger, Guid, Guid, Exception?> PasswordChangedEvent =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, PasswordChangedId, "Password changed: sub {sub}, sid {sid}");

    private static readonly Action<ILogger, Guid, Exception?> PasswordResetEvent =
        LoggerMessage.Define<Guid>(LogLevel.Information, PasswordChangedId, "Password reset: sub {sub}");

    private static readonly EventId EmailChangedId = new(15, "EmailChanged");

    private static readonly Action<ILogger, Guid, Exception?> EmailChangedEvent =
        LoggerMessage.Define<Guid>(LogLevel.Information, EmailChangedId, "Email changed: sub {sub}");

    private static readonly EventId UserDeletedId = new(16, "UserDeleted");

    private static readonly Action<ILogger, Guid, Guid, Exception?> UserDeletedEvent =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, UserDeletedId, "User deleted: sub {sub}, sid {sid}");

    private static readonly EventId RateLimitedId = new(17, "RateLimited");

    private static readonly Action<ILogger, string, Guid, Exception?> RateLimitedWithSub =
        LoggerMessage.Define<string, Guid>(LogLevel.Warning, RateLimitedId, "Rate limited: policy {policy}, sub {sub}");

    private static readonly Action<ILogger, string, Exception?> RateLimitedWithoutSub =
        LoggerMessage.Define<string>(LogLevel.Warning, RateLimitedId, "Rate limited: policy {policy}");

    private static readonly EventId EmailChangeUndoneId = new(19, "EmailChangeUndone");

    private static readonly Action<ILogger, Guid, Exception?> EmailChangeUndoneEvent =
        LoggerMessage.Define<Guid>(LogLevel.Warning, EmailChangeUndoneId, "Email change undone: sub {sub}");

    private static readonly EventId MessageSkippedId = new(18, "MessageSkipped");

    private static readonly Action<ILogger, MessageType, string, Guid, Exception?> MessageSkippedWithSub =
        LoggerMessage.Define<MessageType, string, Guid>(
            LogLevel.Information, MessageSkippedId, "Message skipped: {messageType}, {reason}, sub {sub}");

    private static readonly Action<ILogger, MessageType, string, Exception?> MessageSkippedWithoutSub =
        LoggerMessage.Define<MessageType, string>(
            LogLevel.Information, MessageSkippedId, "Message skipped: {messageType}, {reason}");

    private static readonly Action<ILogger, MessageType, string, Guid, Exception?> MessageSkippedAsWarning =
        LoggerMessage.Define<MessageType, string, Guid>(
            LogLevel.Warning, MessageSkippedId, "Message skipped: {messageType}, {reason}, sub {sub}");

    public static void SignUp(this ILogger logger, Guid userId) =>
        SignUpEvent(logger, userId, null);

    public static void SignInSucceeded(this ILogger logger, Guid userId, Guid sessionId) =>
        SignInSucceededEvent(logger, userId, sessionId, null);

    public static void SignInFailed(this ILogger logger, Guid? userId, bool blocked)
    {
        if (userId is { } sub)
        {
            SignInFailedWithSub(logger, sub, blocked, null);
        }
        else
        {
            SignInFailedWithoutAccount(logger, false, blocked, null);
        }
    }

    public static void SignInBlockStarted(this ILogger logger, Guid userId, int failureCount, DateTime blockedUntil) =>
        SignInBlockStartedEvent(logger, userId, failureCount, blockedUntil, null);

    public static void PasswordCheckFailed(this ILogger logger, Guid userId, Guid sessionId, bool blocked) =>
        PasswordCheckFailedEvent(logger, userId, sessionId, blocked, null);

    public static void PasswordCheckFailed(this ILogger logger, Guid userId, bool blocked) =>
        PasswordCheckFailedWithoutSid(logger, userId, blocked, null);

    public static void PasswordChanged(this ILogger logger, Guid userId, Guid sessionId) =>
        PasswordChangedEvent(logger, userId, sessionId, null);

    public static void PasswordReset(this ILogger logger, Guid userId) =>
        PasswordResetEvent(logger, userId, null);

    public static void EmailChanged(this ILogger logger, Guid userId) =>
        EmailChangedEvent(logger, userId, null);

    public static void EmailChangeUndone(this ILogger logger, Guid userId) =>
        EmailChangeUndoneEvent(logger, userId, null);

    public static void UserDeleted(this ILogger logger, Guid userId, Guid sessionId) =>
        UserDeletedEvent(logger, userId, sessionId, null);

    public static void BreachedCheckUnavailable(this ILogger logger, Exception exception) =>
        BreachedCheckUnavailableEvent(logger, exception);

    public static void SessionRefreshed(this ILogger logger, Guid userId, Guid sessionId, Guid refreshTokenId) =>
        SessionRefreshedEvent(logger, userId, sessionId, refreshTokenId, null);

    public static void RefreshSuperseded(this ILogger logger, Guid userId, Guid sessionId) =>
        RefreshSupersededEvent(logger, userId, sessionId, null);

    public static void RefreshTokenReused(this ILogger logger, Guid userId, Guid sessionId, Guid refreshTokenId) =>
        RefreshTokenReusedEvent(logger, userId, sessionId, refreshTokenId, null);

    public static void RefreshRejected(this ILogger logger, string reason, Guid? sessionId)
    {
        if (sessionId is { } sid)
        {
            RefreshRejectedWithSid(logger, reason, sid, null);
        }
        else
        {
            RefreshRejectedWithoutSid(logger, reason, null);
        }
    }

    public static void SessionEnded(this ILogger logger, Guid userId, Guid sessionId, SessionEndReason reason) =>
        SessionEndedEvent(logger, userId, sessionId, reason, null);

    public static void CsrfRejected(this ILogger logger, FailedCsrfCheck check) =>
        CsrfRejectedEvent(logger, check switch
        {
            FailedCsrfCheck.Header => "header",
            FailedCsrfCheck.Origin => "origin",
            FailedCsrfCheck.FetchSite => "fetch-site",
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, null)
        }, null);

    public static void RateLimited(this ILogger logger, string policy, Guid? userId)
    {
        if (userId is { } sub)
        {
            RateLimitedWithSub(logger, policy, sub, null);
        }
        else
        {
            RateLimitedWithoutSub(logger, policy, null);
        }
    }

    public static void MessageSkipped(this ILogger logger, MessageType type, string reason, Guid? userId)
    {
        if (userId is not { } sub)
        {
            MessageSkippedWithoutSub(logger, type, reason, null);
        }
        else if (reason == MessageSkip.AddressCapped)
        {
            MessageSkippedAsWarning(logger, type, reason, sub, null);
        }
        else
        {
            MessageSkippedWithSub(logger, type, reason, sub, null);
        }
    }

    public static void AccessTokenRejected(this ILogger logger, string reason, string? knownKid)
    {
        if (knownKid is null)
        {
            RejectedWithoutKid(logger, reason, false, null);
        }
        else
        {
            RejectedWithKid(logger, reason, knownKid, null);
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
