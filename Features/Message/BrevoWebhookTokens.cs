using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Laneway.Api;

/// <summary>
/// The bearer Brevo sends with each webhook call. It's a JWT signed with <c>BREVO__WEBHOOKSIGNINGKEY</c>, never the
/// access-token key, so neither kind of token passes as the other.
/// </summary>
public sealed class BrevoWebhookTokens(IOptions<BrevoOptions> options, TimeProvider time)
{
    public const string Audience = "brevo-webhook";

    public const string MintCommand = "mint-brevo-webhook-token";

    public const int MinimumKeyBytes = 32;

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    public static string Mint(IConfiguration configuration)
    {
        var options = configuration.GetSection(BrevoOptions.Section).Get<BrevoOptions>() ?? new BrevoOptions();
        var checkedOptions = new BrevoOptionsValidator().Validate(null, options);
        if (checkedOptions.Failed)
        {
            throw new InvalidOperationException(checkedOptions.FailureMessage);
        }

        return Mint(options.WebhookSigningKey, DateTime.UtcNow);
    }

    public static string Mint(string signingKey, DateTime now) =>
        Handler.CreateToken(new SecurityTokenDescriptor
        {
            Audience = Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            SigningCredentials = new SigningCredentials(Key(signingKey), SecurityAlgorithms.HmacSha256)
        });

    /// <summary>Returns why the Authorization header fails, or null when it holds a valid webhook token.</summary>
    public async Task<string?> RejectionAsync(string? authorization)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookRejection.Missing;
        }

        JsonWebToken token;
        try
        {
            token = new JsonWebToken(authorization[prefix.Length..].Trim());
        }
        catch (ArgumentException)
        {
            return WebhookRejection.Malformed;
        }

        // IdentityModel reports a disallowed algorithm as a signature failure.
        if (token.Alg != SecurityAlgorithms.HmacSha256)
        {
            return WebhookRejection.InvalidAlgorithm;
        }

        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidAudience = Audience,
            IssuerSigningKeys = [Key(options.Value.WebhookSigningKey)],
            TryAllIssuerSigningKeys = false,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            LifetimeValidator = (notBefore, expires, _, _) => InLifetime(notBefore, expires)
        });

        return result.IsValid ? null : Reason(result.Exception);
    }

    private static SymmetricSecurityKey Key(string signingKey) =>
        new(Encoding.UTF8.GetBytes(signingKey)) { KeyId = Audience };

    // IdentityModel checks lifetime against DateTime.UtcNow, which a test clock can't move.
    private bool InLifetime(DateTime? notBefore, DateTime? expires)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (notBefore is not { } from || from == DateTime.MinValue)
        {
            throw new SecurityTokenMalformedException("The token has no nbf.");
        }

        if (from.ToUniversalTime() > now + ClockSkew)
        {
            throw new SecurityTokenNotYetValidException { NotBefore = from };
        }

        if (expires is null || expires.Value.ToUniversalTime() <= now - ClockSkew)
        {
            throw new SecurityTokenExpiredException { Expires = expires ?? DateTime.MinValue };
        }

        return true;
    }

    private static string Reason(Exception exception) => exception switch
    {
        SecurityTokenExpiredException => WebhookRejection.Expired,
        SecurityTokenNotYetValidException => WebhookRejection.NotYetValid,
        SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException =>
            WebhookRejection.InvalidSignature,
        SecurityTokenInvalidAudienceException => WebhookRejection.InvalidAudience,
        _ => WebhookRejection.Malformed
    };
}
