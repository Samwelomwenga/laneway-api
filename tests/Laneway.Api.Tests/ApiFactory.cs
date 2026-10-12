using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Laneway.Api.Tests;

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKeyId = "test";

    public const string SigningKey = "cNDergQ+t0OsqAmWjqRYls/RSPk11lB9DEUWpkpREDU=";

    public const string Issuer = "https://localhost:7287";

    public const string Audience = "laneway-api";

    public const string AllowedOrigin = "https://app.laneway.example";

    private readonly string _connectionString;

    public ApiFactory(string connectionString) => _connectionString = connectionString;

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public PwnedPasswordsStub PwnedPasswords { get; } = new();

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
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Supabase:BucketName"] = TestStack.BucketName,
                ["Auth:SigningKeys:0:Id"] = SigningKeyId,
                ["Auth:SigningKeys:0:Key"] = SigningKey,
                ["Cors:AllowedOrigins:0"] = AllowedOrigin
            }));
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddHttpClient<BreachedPasswords>()
                .ConfigurePrimaryHttpMessageHandler(() => PwnedPasswords)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        });
    }
}
