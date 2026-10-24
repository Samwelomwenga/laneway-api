using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Laneway.Api;

public enum AuthTokenKind
{
    Verification,
    Reset
}

public class AuthToken
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AuthTokenKind Kind { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public static (AuthToken Row, string Value) Issue(Guid userId, AuthTokenKind kind, TimeSpan lifetime, DateTime now)
    {
        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            TokenHash = Hash(value),
            CreatedAt = now,
            ExpiresAt = now + lifetime
        };
        return (row, value);
    }

    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
