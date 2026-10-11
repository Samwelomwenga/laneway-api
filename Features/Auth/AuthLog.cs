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
