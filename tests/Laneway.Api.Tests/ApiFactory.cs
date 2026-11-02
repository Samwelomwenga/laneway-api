using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Laneway.Api.Tests;

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKeyId = "test";

    public const string SigningKey = "cNDergQ+t0OsqAmWjqRYls/RSPk11lB9DEUWpkpREDU=";

    public const string Issuer = "https://localhost:7287";

    public const string Audience = "laneway-api";

    public const string AllowedOrigin = "https://app.laneway.example";

    public const string ClientIpHeader = "X-Test-Client-Ip";

    public const string BrevoApiKey = "xkeysib-test";

    public const string SenderEmail = "no-reply@laneway.test";

    public const string WebhookSigningKey = "brevo-webhook-test-key-that-is-long-enough";

    private readonly string _connectionString;

    public ApiFactory(string connectionString) => _connectionString = connectionString;

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public PwnedPasswordsStub PwnedPasswords { get; } = new();

    public BrevoStub Brevo { get; } = new();

    public Shipped<LogRecord> ShippedLogs { get; } = new();

    public Shipped<Activity> ShippedSpans { get; } = new();

    public Shipped<MetricSnapshot> ShippedMetrics { get; } = new();

    public HttpClient CreateApiClient()
    {
        var client = CreateClient();
        var resolved = Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        if (resolved != _connectionString)
        {
            client.Dispose();
            throw new InvalidOperationException(
                $"The test host kept its own connection string: {resolved}");
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting(TelemetryPipeline.OtlpEndpoint, string.Empty);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Supabase:BucketName"] = TestStack.BucketName,
                ["Auth:SigningKeys:0:Id"] = SigningKeyId,
                ["Auth:SigningKeys:0:Key"] = SigningKey,
                ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                ["WebApp:BaseUrl"] = AllowedOrigin,
                ["Brevo:ApiKey"] = BrevoApiKey,
                ["Brevo:SenderEmail"] = SenderEmail,
                ["Brevo:WebhookSigningKey"] = WebhookSigningKey
            }));
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
        builder.ConfigureTestServices(services =>
        {
            services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, ClientIpFromHeader>());
            services.AddSingleton<TimeProvider>(Time);
            services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
            services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(ShippedLogs));
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(ShippedSpans));
            services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(ShippedMetrics));
            services.AddHttpClient<BreachedPasswords>()
                .ConfigurePrimaryHttpMessageHandler(PwnedPasswords.OverLoopback)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            services.AddHttpClient<IEmailProvider, BrevoEmail>()
                .ConfigurePrimaryHttpMessageHandler(() => Brevo)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        });
    }
}

// TestServer leaves RemoteIpAddress null.
internal sealed class ClientIpFromHeader : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[ApiFactory.ClientIpHeader], out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
