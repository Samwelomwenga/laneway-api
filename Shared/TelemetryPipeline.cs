using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

namespace Laneway.Api;

public static class TelemetryPipeline
{
    public const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // See ADR 0011.
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(
            (services, logger) => logger.ReadFrom.Configuration(services.GetRequiredService<IConfiguration>()),
            preserveStaticLogger: true,
            writeToProviders: true);

        var telemetry = builder.Services.AddOpenTelemetry()
            .WithLogging(configureBuilder: null, configureOptions: options => options.IncludeScopes = true)
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(AuthMeter.Name, DenialMeter.Name, MessageMeter.Name));

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpoint]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }
}
