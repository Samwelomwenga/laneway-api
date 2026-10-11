using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Laneway.Api.Tests;

internal sealed class PwnedPasswordsStub : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, byte> _breached = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<PwnedRequest> _requests = new();

    public Exception? Failure { get; set; }

    public IReadOnlyList<PwnedRequest> Requests => [.. _requests];

    public void Breach(string password) => _breached[Sha1(password)] = 0;

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "The Pwned Passwords range API indexes passwords by SHA-1.")]
    public static string Sha1(string password) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _requests.Enqueue(new PwnedRequest(
            request.RequestUri!,
            request.Headers.TryGetValues("Add-Padding", out var padding) ? string.Join(",", padding) : null));
        if (Failure is { } failure)
        {
            throw failure;
        }

        var prefix = request.RequestUri!.Segments[^1];
        var lines = _breached.Keys
            .Where(hash => hash.StartsWith(prefix, StringComparison.Ordinal))
            .Select(hash => $"{hash[5..]}:3")
            .Append($"{new string('0', 35)}:0");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join("\r\n", lines))
        });
    }
}

internal sealed record PwnedRequest(Uri Uri, string? Padding);
