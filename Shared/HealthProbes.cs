using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Laneway.Api;

public static class HealthProbes
{
    public const string Live = "/health/live";

    public const string Ready = "/health/ready";

    private const string ReadyTag = "ready";

    public static IServiceCollection AddHealthProbes(this IServiceCollection services, string connectionString)
    {
        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgres", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(2));
        return services;
    }

    public static IEndpointRouteBuilder MapHealthProbes(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(Live, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks(Ready, new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous();
        return endpoints;
    }

    public static bool IsProbe(PathString path) =>
        path.Equals(Live, StringComparison.OrdinalIgnoreCase) || path.Equals(Ready, StringComparison.OrdinalIgnoreCase);
}
