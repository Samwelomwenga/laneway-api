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

    public bool Verify(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hash(password);
        }

        return result != PasswordVerificationResult.Failed;
    }

    public void VerifyDummy(string password) => _hasher.VerifyHashedPassword(null!, _dummyHash, password);
}
