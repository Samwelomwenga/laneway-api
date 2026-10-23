using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Laneway.Api.Tests;

public class MessageStartupTests
{
    [Theory]
    [InlineData("Brevo:ApiKey", "BREVO__APIKEY")]
    [InlineData("Brevo:SenderEmail", "BREVO__SENDEREMAIL")]
    [InlineData("Brevo:WebhookSigningKey", "BREVO__WEBHOOKSIGNINGKEY")]
    public void A_missing_brevo_setting_stops_the_app_at_startup(string setting, string variable)
    {
        var thrown = StartWith(setting, string.Empty);

        Assert.Contains(variable, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_webhook_signing_key_under_32_bytes_stops_the_app_at_startup()
    {
        var thrown = StartWith("Brevo:WebhookSigningKey", new string('k', 31));

        Assert.Contains("BREVO__WEBHOOKSIGNINGKEY is 31 bytes", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://elsewhere.laneway.example")]
    [InlineData("https://app.laneway.example/")]
    [InlineData("https://app.laneway.example/app")]
    [InlineData("")]
    public void A_bad_web_app_base_url_stops_the_app_at_startup(string baseUrl)
    {
        var thrown = StartWith("WebApp:BaseUrl", baseUrl);

        Assert.Contains("WEBAPP__BASEURL", thrown.Message, StringComparison.Ordinal);
    }

    private static OptionsValidationException StartWith(string setting, string value)
    {
        using var factory = new ApiFactory("Host=localhost;Database=never_opened");
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [setting] = value
            })));

        return Assert.Throws<OptionsValidationException>(() => broken.CreateClient());
    }
}
