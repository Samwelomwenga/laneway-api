using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Laneway.Api.Tests;

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiFactory(string connectionString) => _connectionString = connectionString;

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
                ["Supabase:BucketName"] = TestStack.BucketName
            }));
    }
}
