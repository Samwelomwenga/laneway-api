using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Laneway.Api;

public sealed class BreachedPasswords(HttpClient client)
{
    private const int PrefixLength = 5;

    public static void Configure(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
        client.Timeout = TimeSpan.FromSeconds(2);
        client.DefaultRequestHeaders.Add("Add-Padding", "true");
    }

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "The Pwned Passwords range API indexes passwords by SHA-1.")]
    public async Task<bool> ContainsAsync(string password)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var suffixes = await client.GetStringAsync(new Uri($"range/{hash[..PrefixLength]}", UriKind.Relative));

        foreach (var line in suffixes.AsSpan().EnumerateLines())
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Equals(hash.AsSpan(PrefixLength), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
