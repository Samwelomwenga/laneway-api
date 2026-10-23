using Microsoft.Extensions.Options;

namespace Laneway.Api;

public sealed class WebAppOptions
{
    public const string Section = "WebApp";

    public string BaseUrl { get; set; } = string.Empty;
}

// Reading IOptions<CorsOrigins> here would run its validator a second time and double its errors.
public sealed class WebAppOptionsValidator(IHostEnvironment environment, IConfiguration configuration)
    : IValidateOptions<WebAppOptions>
{
    public ValidateOptionsResult Validate(string? name, WebAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!CorsOriginsValidator.IsAllowedOrigin(options.BaseUrl, environment))
        {
            return ValidateOptionsResult.Fail(
                $"WEBAPP__BASEURL '{options.BaseUrl}' must be an exact https origin, such as https://app.laneway.example, or, in Development only, an http loopback origin, such as http://localhost:3000.");
        }

        var allowed = configuration.GetSection(CorsOrigins.Section).Get<CorsOrigins>()?.AllowedOrigins ?? [];
        return allowed.Contains(options.BaseUrl, StringComparer.OrdinalIgnoreCase)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"WEBAPP__BASEURL '{options.BaseUrl}' must also appear in {CorsOrigins.Section}:AllowedOrigins.");
    }
}
