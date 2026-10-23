using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Laneway.Api.Tests;

internal sealed class BrevoStub : HttpMessageHandler
{
    private readonly ConcurrentQueue<BrevoSend> _sends = new();

    private readonly SemaphoreSlim _sent = new(0);

    public IReadOnlyList<BrevoSend> Sends => [.. _sends];

    public async Task<BrevoSend> AwaitEmailAsync(string to)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (_sends.FirstOrDefault(send => send.To == to) is { } found)
            {
                return found;
            }

            try
            {
                await _sent.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"No email reached Brevo for {to} within 5 seconds.");
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = body.RootElement;
        var send = new BrevoSend(
            request.RequestUri!,
            request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null,
            root.GetProperty("sender").GetProperty("name").GetString()!,
            root.GetProperty("sender").GetProperty("email").GetString()!,
            root.GetProperty("to").EnumerateArray().Single().GetProperty("email").GetString()!,
            root.GetProperty("subject").GetString()!,
            root.GetProperty("htmlContent").GetString()!,
            root.GetProperty("textContent").GetString()!,
            root.GetProperty("headers").GetProperty("idempotencyKey").GetString()!,
            [.. root.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()!)],
            $"<{Guid.NewGuid():n}@smtp-relay.mailin.fr>",
            HttpStatusCode.Created);

        _sends.Enqueue(send);
        _sent.Release();
        return new HttpResponseMessage(send.Status)
        {
            Content = JsonContent.Create(new { messageId = send.MessageId })
        };
    }
}

internal sealed record BrevoSend(
    Uri Uri,
    string? ApiKey,
    string SenderName,
    string SenderEmail,
    string To,
    string Subject,
    string Html,
    string Text,
    string IdempotencyKey,
    IReadOnlyList<string> Tags,
    string MessageId,
    HttpStatusCode Status)
{
    public string TokenFrom(string route)
    {
        var prefix = $"{route}#token=";
        foreach (var line in Text.Split('\n'))
        {
            var at = line.IndexOf(prefix, StringComparison.Ordinal);
            if (at >= 0)
            {
                return Uri.UnescapeDataString(line[(at + prefix.Length)..].Trim());
            }
        }

        throw new InvalidOperationException($"The email to {To} has no link to {route}.");
    }
}
