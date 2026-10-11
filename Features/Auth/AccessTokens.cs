using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Laneway.Api;

public sealed class AccessTokens(AuthOptions options, SigningKeys keys, TimeProvider time)
{
    public const string TokenType = "at+jwt";

    public const string SessionClaim = "sid";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public string Issue(Guid userId, Guid sessionId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
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
    }
}
