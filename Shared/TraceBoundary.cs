using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Laneway.Api;

public static class TraceBoundary
{
    public static IServiceCollection AddTraceBoundary(this IServiceCollection services)
    {
        DistributedContextPropagator.Current = NoTraceContext.Instance;
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator([]));
        services.Configure<ProblemDetailsOptions>(QuoteTraceIdentifier);
        return services.Replace(ServiceDescriptor.Singleton<DistributedContextPropagator>(NoTraceContext.Instance));
    }

    public static IApplicationBuilder UseTraceIdentifier(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (Activity.Current is { } request)
            {
                context.TraceIdentifier = request.TraceId.ToHexString();
            }

            return next(context);
        });

    // ProblemDetails defaults to Activity.Id, the whole traceparent, so it would quote a different id from the 500 body.
    private static void QuoteTraceIdentifier(ProblemDetailsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    }
}

internal sealed class NoTraceContext : DistributedContextPropagator
{
    public static NoTraceContext Instance { get; } = new();

    public override IReadOnlyCollection<string> Fields { get; } = [];

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
    {
    }

    public override void ExtractTraceIdAndState(
        object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
    {
        traceId = null;
        traceState = null;
    }

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(
        object? carrier, PropagatorGetterCallback? getter) => null;
}
