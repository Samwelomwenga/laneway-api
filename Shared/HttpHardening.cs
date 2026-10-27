using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Laneway.Api;

public static class HttpHardening
{
    private const string UseHttps = "Use HTTPS.";

    public static IServiceCollection AddHttpHardening(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("ForwardedHeaders");
        var proxies = (section.GetSection("KnownProxies").Get<string[]>() ?? []).Select(IPAddress.Parse).ToList();
        var networks = (section.GetSection("KnownNetworks").Get<string[]>() ?? [])
            .Select(network => Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(network))
            .ToList();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();
            proxies.ForEach(options.KnownProxies.Add);
            networks.ForEach(options.KnownNetworks.Add);
        });

        return services;
    }

    // ForwardedHeadersMiddleware checks the caller against the trusted lists only when one has an entry, and
    // otherwise believes every caller. With nothing trusted, it stays out and the socket address stands.
    public static IApplicationBuilder UseTrustedForwardedHeaders(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        return options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0
            ? app
            : app.UseForwardedHeaders();
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IHostEnvironment environment) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.CacheControl = "no-store";
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                if (!(environment.IsDevelopment() && IsDocs(context.Request.Path)))
                {
                    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                }

                if (!environment.IsDevelopment() && context.Request.IsHttps)
                {
                    headers.StrictTransportSecurity = "max-age=63072000; includeSubDomains";
                }

                return Task.CompletedTask;
            });

            return next(context);
        });

    public static IApplicationBuilder RefusePlainHttp(this IApplicationBuilder app, IHostEnvironment environment) =>
        environment.IsDevelopment()
            ? app
            : app.Use(async (context, next) =>
            {
                if (context.Request.IsHttps)
                {
                    await next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteApiResponseAsync(
                    ApiResponse<object>.ErrorResponse(UseHttps, StatusCodes.Status400BadRequest,
                        [new ApiError(null, ErrorCodes.HttpsRequired, UseHttps)]),
                    context.RequestAborted);
            });

    private static bool IsDocs(PathString path) =>
        path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi");
}
