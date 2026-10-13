using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Laneway.Api;

public sealed class JwtBearerSetup(IOptions<AuthOptions> auth, SigningKeys keys, TimeProvider time)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.IncludeErrorDetails = false;
        options.TimeProvider = time;
        options.EventsType = typeof(BearerEvents);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth.Value.Issuer,
            ValidAudience = auth.Value.Audience,
            IssuerSigningKeys = keys.All,
            TryAllIssuerSigningKeys = false,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidTypes = [AccessTokens.TokenType],
            ClockSkew = AccessTokens.ClockSkew,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            NameClaimType = JwtRegisteredClaimNames.Sub,
            LifetimeValidator = (_, expires, _, parameters) => Unexpired(expires, parameters)
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    // IdentityModel checks lifetime against DateTime.UtcNow, which a test clock can't move.
    private bool Unexpired(DateTime? expires, TokenValidationParameters parameters)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (expires is null || expires.Value.ToUniversalTime() + parameters.ClockSkew <= now)
        {
            throw new SecurityTokenExpiredException { Expires = expires ?? DateTime.MinValue };
        }

        return true;
    }
}
