using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Laneway.Api.Tests;

internal sealed class BrevoStub : HttpMessageHandler
{
    private readonly ConcurrentQueue<BrevoSend> _sends = new();

    private readonly ConcurrentQueue<BrevoAnswer> _answers = new();

    private readonly SemaphoreSlim _sent = new(0);

    private TaskCompletionSource _held = Released();

    private int _inFlight;

    private int _maxInFlight;

    public IReadOnlyList<BrevoSend> Sends => [.. _sends];

    public int InFlight => Volatile.Read(ref _inFlight);

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public void Answer(params BrevoAnswer[] answers)
    {
        foreach (var answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    public void Hold() => _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _held.TrySetResult();

    public Task<BrevoSend> AwaitEmailAsync(string to) =>
        AwaitAsync(sends => sends.FirstOrDefault(send => send.To == to && send.Status == HttpStatusCode.Created),
            $"No email reached Brevo for {to} within 5 seconds.");

    public Task<BrevoSend> AwaitEmailAsync(string to, string tag, int nth = 1) =>
        AwaitAsync(
            sends => sends
                .Where(send => send.To == to && send.Status == HttpStatusCode.Created && send.Tags.Contains(tag))
                .Skip(nth - 1)
                .FirstOrDefault(),
            $"No {tag} email number {nth} reached Brevo for {to} within 5 seconds.");

    public Task<IReadOnlyList<BrevoSend>> AwaitSendsAsync(int count) =>
        AwaitAsync(sends => sends.Count >= count ? sends : null,
            $"Brevo didn't see {count} sends within 5 seconds.");

    public async Task AwaitInFlightAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (InFlight < count)
        {
            try
            {
                await _sent.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Brevo didn't hold {count} sends at once within 5 seconds.");
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inFlight = Interlocked.Increment(ref _inFlight);
        InterlockedMax(ref _maxInFlight, inFlight);
        _sent.Release();
        try
        {
            await _held.Task.WaitAsync(cancellationToken);
            return await AnswerAsync(request, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var answer = _answers.TryDequeue(out var queued) ? queued : HttpStatusCode.Created;
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
            answer.Status);

        _sends.Enqueue(send);
        _sent.Release();
        if (answer.TimedOut)
        {
            throw new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());
        }

        if (answer.Status is not { } status)
        {
            throw new HttpRequestException("Connection refused (api.brevo.com:443)");
        }

        return new HttpResponseMessage(status)
        {
            Content = status == HttpStatusCode.Created
                ? JsonContent.Create(new { messageId = send.MessageId })
                : JsonContent.Create(new { code = answer.Code ?? "bad_request", message = $"{send.To} was refused" })
        };
    }

    private static TaskCompletionSource Released()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        released.SetResult();
        return released;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var seen = Volatile.Read(ref target);
        while (value > seen)
        {
            var previous = Interlocked.CompareExchange(ref target, value, seen);
            if (previous == seen)
            {
                return;
            }

            seen = previous;
        }
    }

    private async Task<T> AwaitAsync<T>(Func<IReadOnlyList<BrevoSend>, T?> find, string timeoutMessage)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (find(Sends) is { } found)
            {
                return found;
            }

            try
            {
                await _sent.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(timeoutMessage);
            }
        }
    }
}

internal sealed record BrevoAnswer(HttpStatusCode? Status, string? Code = null, bool TimedOut = false)
{
    public static BrevoAnswer Timeout { get; } = new(null, TimedOut: true);

    public static BrevoAnswer NetworkError { get; } = new(null);

    public static implicit operator BrevoAnswer(HttpStatusCode status) => new(status);
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
    HttpStatusCode? Status)
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
