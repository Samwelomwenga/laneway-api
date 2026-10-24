using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Laneway.Api;

public enum AuthTokenKind
{
    Verification,
    Reset,
    ChangeEmail,
    Undo
}

public class AuthToken
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public static readonly TimeSpan ChangeEmailLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan UndoLifetime = TimeSpan.FromDays(7);

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AuthTokenKind Kind { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>The address a change-email token moves to, or the one an undo token restores. Never on <see cref="User"/>.</summary>
    public string? Email { get; set; }

    public static (AuthToken Row, string Value) Issue(
        Guid userId, AuthTokenKind kind, TimeSpan lifetime, DateTime now, string? email = null)
    {
        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            TokenHash = Hash(value),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
            Email = email
        };
        return (row, value);
    }

    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
