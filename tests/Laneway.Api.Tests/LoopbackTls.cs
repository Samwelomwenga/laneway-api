using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Laneway.Api.Tests;

// SocketsHttpHandler starts the HttpClient span, so a stub that replaces it ships no span.
internal static class LoopbackTls
{
    private static readonly Lazy<X509Certificate2> Certificate = new(CreateCertificate);

    public static SocketsHttpHandler Handler(Func<string, string> answer) => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await client.ConnectAsync(listener.LocalEndpoint, cancellationToken);
                var server = await listener.AcceptSocketAsync(cancellationToken);
                _ = ServeAsync(server, answer);
                return new NetworkStream(client, ownsSocket: true);
            }
            finally
            {
                listener.Stop();
            }
        },
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate?.GetCertHashString() == Certificate.Value.GetCertHashString()
        }
    };

    private static async Task ServeAsync(Socket server, Func<string, string> answer)
    {
        await using var stream = new SslStream(new NetworkStream(server, ownsSocket: true));
        await stream.AuthenticateAsServerAsync(Certificate.Value);

        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync() ?? string.Empty;
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        var body = Encoding.UTF8.GetBytes(answer(requestLine.Split(' ')[1]));
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=loopback", key, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
    }
}
