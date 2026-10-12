using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Laneway.Api;

public class RefreshToken
{
    public const string Prefix = "lnw_rt_";

    public static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(10);

    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RetiredAt { get; set; }

    public static (RefreshToken Row, string Value) Issue(Guid sessionId, DateTime now)
    {
        var value = Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TokenHash = Hash(value),
            CreatedAt = now
        };
        return (row, value);
    }

    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
