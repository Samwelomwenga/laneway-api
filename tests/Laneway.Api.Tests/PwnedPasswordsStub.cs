using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Laneway.Api.Tests;

internal sealed class PwnedPasswordsStub : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, byte> _breached = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<PwnedRequest> _requests = new();

    private readonly ConcurrentQueue<IReadOnlyList<string>> _wireHeaderNames = new();

    public Exception? Failure { get; set; }

    public Func<Task>? BeforeAnswer { get; set; }

    public IReadOnlyList<PwnedRequest> Requests => [.. _requests];

    public IReadOnlyList<IReadOnlyList<string>> WireHeaderNames => [.. _wireHeaderNames];

    // IHttpClientFactory builds its SocketsHttpHandler with the pipeline, after the host set the process-wide
    // propagator, so the loopback handler waits for the pipeline too.
    public PwnedPasswordsStub OverLoopback()
    {
        InnerHandler ??= LoopbackTls.Handler(Answer);
        return this;
    }

    public void Breach(string password) => _breached[Sha1(password)] = 0;

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "The Pwned Passwords range API indexes passwords by SHA-1.")]
    public static string Sha1(string password) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (BeforeAnswer is { } beforeAnswer)
        {
            await beforeAnswer();
        }

        _requests.Enqueue(new PwnedRequest(
            request.RequestUri!,
            request.Headers.TryGetValues("Add-Padding", out var padding) ? string.Join(",", padding) : null));
        if (Failure is { } failure)
        {
            throw failure;
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private string Answer(string path, IReadOnlyList<string> headerNames)
    {
        _wireHeaderNames.Enqueue(headerNames);
        var prefix = path[(path.LastIndexOf('/') + 1)..];
        var lines = _breached.Keys
            .Where(hash => hash.StartsWith(prefix, StringComparison.Ordinal))
            .Select(hash => $"{hash[5..]}:3")
            .Append($"{new string('0', 35)}:0");
        return string.Join("\r\n", lines);
    }
}

internal sealed record PwnedRequest(Uri Uri, string? Padding);
