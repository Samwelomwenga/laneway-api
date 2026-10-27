using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Laneway.Api;

public sealed class InviteUnsubscribes(
    IDataProtectionProvider protection,
    ApplicationDbContext database,
    TimeProvider time,
    MessageMeter meter,
    ILoggerFactory loggers)
{
    public const string MessageId = "unsubscribe-link";

    private readonly IDataProtector _protector = protection.CreateProtector("Laneway.InviteUnsubscribe");

    private readonly ILogger _log = loggers.CreateLogger(MessageLog.Category);

    public string TokenFor(string email) => _protector.Protect(email);

    /// <summary>
    /// Writes the same invite-only suppression an <c>unsubscribed</c> webhook event does. A repeat writes nothing
    /// and still succeeds.
    /// </summary>
    public async Task<ApiResponse<bool>> UnsubscribeAsync(string token, CancellationToken cancellation)
    {
        string email;
        try
        {
            email = _protector.Unprotect(token);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return ApiResponse<bool>.ErrorResponse("Invalid data", StatusCodes.Status400BadRequest,
                [new ApiError("token", ErrorCodes.InvalidToken, "This link is invalid.")]);
        }

        var added = await Suppressions.AddAsync(database, Suppressions.Hash(email), SuppressionKind.Unsubscribed, MessageId,
            time.GetUtcNow().UtcDateTime, cancellation);
        if (added)
        {
            meter.Suppressed(SuppressionKind.Unsubscribed);
        }

        _log.InvitesUnsubscribed(alreadySuppressed: !added);

        return ApiResponse<bool>.SuccessResponse(true, "Unsubscribed from invites", StatusCodes.Status204NoContent);
    }
}
