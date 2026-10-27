using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Laneway.Api.Tests;

public class LogMuteStartupTests
{
    [Theory]
    [InlineData("Serilog:MinimumLevel", "Warning")]
    [InlineData("Serilog:MinimumLevel:Default", "Warning")]
    [InlineData("Serilog:MinimumLevel:Override:Laneway", "Error")]
    [InlineData("Serilog:MinimumLevel:Override:Laneway.Auth", "Warning")]
    [InlineData("Logging:OpenTelemetry:LogLevel:Default", "Warning")]
    [InlineData("Logging:OpenTelemetry:LogLevel:Laneway.Access", "None")]
    [InlineData("Logging:OpenTelemetry:LogLevel:Lane", "Critical")]
    [InlineData("Logging:OpenTelemetry:LogLevel:*", "Warning")]
    [InlineData("Serilog:MinimumLevel:Override:Laneway.Message", "3")]
    public void A_setting_that_mutes_a_laneway_category_stops_the_app_at_startup(string setting, string level)
    {
        using var factory = StartWith(new() { [setting] = level });

        var thrown = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(setting, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_muted_default_with_laneway_kept_at_information_starts()
    {
        using var factory = StartWith(new()
        {
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Serilog:MinimumLevel:Override:Laneway"] = "Information"
        });

        Assert.Null(Record.Exception(() => factory.CreateClient().Dispose()));
    }

    [Theory]
    [InlineData("Serilog:MinimumLevel:Override:Laneway.Auth", "Debug")]
    [InlineData("Logging:OpenTelemetry:LogLevel:Laneway.Message", "Trace")]
    [InlineData("Logging:OpenTelemetry:LogLevel:Microsoft", "Warning")]
    public void A_setting_that_keeps_laneway_at_information_or_below_starts(string setting, string level)
    {
        using var factory = StartWith(new() { [setting] = level });

        Assert.Null(Record.Exception(() => factory.CreateClient().Dispose()));
    }

    private static MutedFactory StartWith(Dictionary<string, string?> settings) => new(settings);

    private sealed class MutedFactory : IDisposable
    {
        private readonly ApiFactory _factory = new("Host=localhost;Database=never_opened");
        private readonly WebApplicationFactory<Program> _muted;

        public MutedFactory(Dictionary<string, string?> settings) =>
            _muted = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
                (_, configuration) => configuration.AddInMemoryCollection(settings)));

        public HttpClient CreateClient() => _muted.CreateClient();

        public void Dispose()
        {
            _muted.Dispose();
            _factory.Dispose();
        }
    }
}
