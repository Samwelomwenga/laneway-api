using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Laneway.Api;

public sealed class AccessTokens(IOptions<AuthOptions> options, SigningKeys keys, TimeProvider time)
{
    public const string TokenType = "at+jwt";

    public const string SessionClaim = "sid";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public IssuedAccessToken Issue(Guid userId, Guid sessionId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now,
            Expires = now + Lifetime,
            TokenType = TokenType,
            SigningCredentials = keys.Signing,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [SessionClaim] = sessionId.ToString()
            }
        });
        return new IssuedAccessToken(token, now + Lifetime);
    }
}

public sealed record IssuedAccessToken(string Value, DateTime ExpiresAt);
