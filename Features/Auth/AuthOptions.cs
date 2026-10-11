namespace Laneway.Api;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public List<SigningKeyOptions> SigningKeys { get; set; } = [];

    public static AuthOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(Section).Get<AuthOptions>() ?? new AuthOptions();
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException(
                $"{Section}:Issuer and {Section}:Audience belong in appsettings.{{Environment}}.json.");
        }

        return options;
    }
}

public sealed class SigningKeyOptions
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}
