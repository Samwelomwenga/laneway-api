using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Laneway.Api;

public sealed class Passwords
{
    public const int Iterations = 220_000;

    public const int MinimumLength = 8;

    public const int MaximumLength = 128;

    private readonly PasswordHasher<User> _hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = Iterations
    }));

    private readonly string _dummyHash;

    public Passwords() => _dummyHash = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    [return: NotNullIfNotNull(nameof(password))]
    public static string? Normalize(string? password) => password?.Normalize(NormalizationForm.FormKC);

    public static int Length(string password) => password.EnumerateRunes().Count();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordCheck Verify(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);

        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
        {
            PasswordVerificationResult.Failed => new PasswordCheck(false, user.PasswordHash),
            PasswordVerificationResult.SuccessRehashNeeded => new PasswordCheck(true, Hash(password)),
            _ => new PasswordCheck(true, user.PasswordHash)
        };
    }

    public void VerifyDummy(string password) => _hasher.VerifyHashedPassword(null!, _dummyHash, password);
}

public readonly record struct PasswordCheck(bool Matched, string HashToStore);
