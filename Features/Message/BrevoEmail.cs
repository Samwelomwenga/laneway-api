using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Laneway.Api;

public sealed class BrevoOptions
{
    public const string Section = "Brevo";

    public string ApiKey { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string WebhookSigningKey { get; set; } = string.Empty;
}

public sealed class BrevoOptionsValidator : IValidateOptions<BrevoOptions>
{
    public ValidateOptionsResult Validate(string? name, BrevoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var problems = new[]
            {
                ("BREVO__APIKEY", options.ApiKey),
                ("BREVO__SENDEREMAIL", options.SenderEmail),
                ("BREVO__WEBHOOKSIGNINGKEY", options.WebhookSigningKey)
            }
            .Where(setting => string.IsNullOrWhiteSpace(setting.Item2))
            .Select(setting =>
                $"{setting.Item1} is missing. Run the app through `doppler run --` or set the {setting.Item1} environment variable.")
            .ToList();

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

public sealed class BrevoEmail(HttpClient client, IOptions<BrevoOptions> options) : IEmailProvider
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly HashSet<string> KnownCodes = new(StringComparer.Ordinal)
    {
        "invalid_parameter",
        "missing_parameter",
        "out_of_range",
        "unauthorized",
        "document_not_found",
        "method_not_allowed",
        "not_enough_credits",
        "duplicate_parameter",
        "duplicate_request",
        "account_under_validation",
        "permission_denied"
    };

    public static void Configure(IServiceProvider services, HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);

        client.BaseAddress = new Uri("https://api.brevo.com/v3/");
        client.Timeout = Timeout;
        client.DefaultRequestHeaders.Add("api-key", services.GetRequiredService<IOptions<BrevoOptions>>().Value.ApiKey);
    }

    public async Task<EmailSent> SendAsync(EmailContent content, Guid idempotencyKey, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var response = await client.PostAsJsonAsync(
            new Uri("smtp/email", UriKind.Relative),
            new
            {
                sender = new { name = "Laneway", email = options.Value.SenderEmail },
                to = new[] { new { email = content.To } },
                subject = content.Subject,
                htmlContent = content.Html,
                textContent = content.Text,
                tags = new[] { content.Tag },
                headers = new { idempotencyKey = idempotencyKey.ToString() }
            },
            token);
        if (!response.IsSuccessStatusCode)
        {
            throw new EmailProviderException((int)response.StatusCode, await KnownCodeAsync(response, token));
        }

        var sent = await response.Content.ReadFromJsonAsync<BrevoSent>(token);
        return new EmailSent(sent?.MessageId ?? throw new InvalidOperationException("Brevo answered without a messageId."));
    }

    private static async Task<string?> KnownCodeAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<BrevoError>(token);
            return error?.Code is { } code && KnownCodes.Contains(code) ? code : null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record BrevoSent(string? MessageId);

    private sealed record BrevoError(string? Code);
}
