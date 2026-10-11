using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Laneway.Api;

public static class BearerTokens
{
    public static IServiceCollection AddBearerTokens(this IServiceCollection services)
    {
        services.AddOptions<AuthOptions>()
            .BindConfiguration(AuthOptions.Section)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddSingleton(provider =>
            SigningKeys.From(provider.GetRequiredService<IOptions<AuthOptions>>().Value.SigningKeys));
        services.AddSingleton<AccessTokens>();
        services.AddSingleton<AuthMeter>();
        services.AddScoped<BearerEvents>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.ConfigureOptions<JwtBearerSetup>();

        return services;
    }

    public static IApplicationBuilder RefuseFailedBearerTokens(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.Authorization.Count > 0 && context.User.Identity?.IsAuthenticated != true)
            {
                await context.ChallengeAsync();
                return;
            }

            await next();
        });
}

public sealed class BearerEvents(
    ApplicationDbContext database,
    Actor actor,
    SigningKeys keys,
    TimeProvider time,
    ILoggerFactory loggers) : JwtBearerEvents
{
    private const string SignInAgain = "Sign in again.";

    private readonly ILogger _log = loggers.CreateLogger(AuthLog.Category);

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var token = (JsonWebToken)context.SecurityToken;
        if (!Guid.TryParse(token.Subject, out var userId)
            || !token.TryGetPayloadValue<string>(AccessTokens.SessionClaim, out var sid)
            || !Guid.TryParse(sid, out var sessionId))
        {
            Reject(context, AccessTokenRejection.Malformed, token.Kid);
            return;
        }

        var live = await database.Sessions
            .Where(Session.ActiveAt(time.GetUtcNow().UtcDateTime))
            .AnyAsync(
                session => session.Id == sessionId
                    && session.UserId == userId
                    && database.Users.Any(user => user.Id == session.UserId),
                context.HttpContext.RequestAborted);
        if (!live)
        {
            Reject(context, AccessTokenRejection.SessionEnded, token.Kid);
            return;
        }

        actor.Resolve(userId);
    }

    public override Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _log.AccessTokenRejected(Reason(context.Exception), KnownKid(KidOf(context.Request)));
        return Task.CompletedTask;
    }

    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.HandleResponse();
        var response = context.Response;
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
        await response.WriteAsJsonAsync(
            ApiResponse<object>.ErrorResponse(SignInAgain, StatusCodes.Status401Unauthorized,
                [new ApiError(null, ErrorCodes.NotAuthenticated, SignInAgain)]),
            context.HttpContext.RequestAborted);
    }

    private void Reject(TokenValidatedContext context, string reason, string? kid)
    {
        _log.AccessTokenRejected(reason, KnownKid(kid));
        context.Fail(reason);
    }

    private string? KnownKid(string? kid) => keys.Has(kid) ? kid : null;

    private static string? KidOf(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return new JsonWebToken(header[prefix.Length..].Trim()).Kid;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string Reason(Exception exception) => exception switch
    {
        SecurityTokenExpiredException => AccessTokenRejection.Expired,
        SecurityTokenSignatureKeyNotFoundException => AccessTokenRejection.UnknownKey,
        SecurityTokenInvalidAlgorithmException => AccessTokenRejection.InvalidAlgorithm,
        SecurityTokenInvalidSignatureException => AccessTokenRejection.InvalidSignature,
        SecurityTokenInvalidTypeException => AccessTokenRejection.InvalidType,
        SecurityTokenInvalidIssuerException => AccessTokenRejection.InvalidIssuer,
        SecurityTokenInvalidAudienceException => AccessTokenRejection.InvalidAudience,
        SecurityTokenMalformedException or ArgumentException => AccessTokenRejection.Malformed,
        _ => AccessTokenRejection.Invalid
    };
}
