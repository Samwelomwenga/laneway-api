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

    private static readonly Action<ILogger, Guid, Guid, Exception?> SignUpEvent =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, SignUpId, "Signed up: sub {sub}, sid {sid}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> SignInSucceededEvent =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, SignInSucceededId, "Signed in: sub {sub}, sid {sid}");

    private static readonly Action<ILogger, Guid, Exception?> SignInFailedWithSub =
        LoggerMessage.Define<Guid>(LogLevel.Warning, SignInFailedId, "Sign-in failed: sub {sub}");

    private static readonly Action<ILogger, bool, Exception?> SignInFailedWithoutAccount =
        LoggerMessage.Define<bool>(LogLevel.Warning, SignInFailedId, "Sign-in failed: accountFound {accountFound}");

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

    public static void SignUp(this ILogger logger, Guid userId, Guid sessionId) =>
        SignUpEvent(logger, userId, sessionId, null);

    public static void SignInSucceeded(this ILogger logger, Guid userId, Guid sessionId) =>
        SignInSucceededEvent(logger, userId, sessionId, null);

    public static void SignInFailed(this ILogger logger, Guid? userId)
    {
        if (userId is { } sub)
        {
            SignInFailedWithSub(logger, sub, null);
        }
        else
        {
            SignInFailedWithoutAccount(logger, false, null);
        }
    }

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

public static class RefreshRejection
{
    public const string MissingCookie = "missingCookie";
    public const string UnknownToken = "unknownToken";
    public const string SessionEnded = "sessionEnded";
}
