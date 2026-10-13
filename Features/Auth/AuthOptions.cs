using Microsoft.Extensions.Options;

namespace Laneway.Api;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public List<SigningKeyOptions> SigningKeys { get; set; } = [];
}

public sealed class SigningKeyOptions
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var problems = SigningKeys.Problems(options.SigningKeys).ToList();
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            problems.Add($"{AuthOptions.Section}:Issuer and {AuthOptions.Section}:Audience belong in appsettings.{{Environment}}.json.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
