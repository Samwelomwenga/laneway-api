using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Laneway.Api;

public sealed class CorsOrigins
{
    public const string Section = "Cors";

    public List<string> AllowedOrigins { get; set; } = [];
}

// WithOrigins never matches an entry with a trailing slash or a path, and says nothing about it.
public sealed class CorsOriginsValidator : IValidateOptions<CorsOrigins>
{
    public ValidateOptionsResult Validate(string? name, CorsOrigins options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var problems = options.AllowedOrigins
            .Where(origin => !IsAllowedOrigin(origin))
            .Select(origin =>
                $"{CorsOrigins.Section}:AllowedOrigins entry '{origin}' must be an exact https origin, such as https://app.laneway.example, or an http loopback origin, such as http://localhost:3000.")
            .ToList();

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private static bool IsAllowedOrigin(string origin) =>
        !origin.Contains('*', StringComparison.Ordinal)
        && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}

public static class BrowserCors
{
    public static IServiceCollection AddBrowserCors(this IServiceCollection services)
    {
        services.AddOptions<CorsOrigins>()
            .BindConfiguration(CorsOrigins.Section)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CorsOrigins>, CorsOriginsValidator>();

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsOrigins>>((cors, origins) =>
            cors.AddDefaultPolicy(policy => policy
                .WithOrigins([.. origins.Value.AllowedOrigins])
                .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete)
                .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType, CsrfCheck.Header)
                .AllowCredentials()
                .WithExposedHeaders(HeaderNames.Location, HeaderNames.RetryAfter)
                .SetPreflightMaxAge(TimeSpan.FromHours(2))));

        return services;
    }
}
