using System.Net.Http.Json;
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
    public static void Configure(IServiceProvider services, HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);

        client.BaseAddress = new Uri("https://api.brevo.com/v3/");
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
        response.EnsureSuccessStatusCode();

        var sent = await response.Content.ReadFromJsonAsync<BrevoSent>(token);
        return new EmailSent(sent?.MessageId ?? throw new InvalidOperationException("Brevo answered without a messageId."));
    }

    private sealed record BrevoSent(string? MessageId);
}
