using Microsoft.IdentityModel.Tokens;

namespace Laneway.Api;

public sealed class SigningKeys
{
    public const int MinimumBytes = 32;

    private SigningKeys(IReadOnlyList<SymmetricSecurityKey> all)
    {
        All = all;
        Signing = new SigningCredentials(all[0], SecurityAlgorithms.HmacSha256);
    }

    public IReadOnlyList<SymmetricSecurityKey> All { get; }

    public SigningCredentials Signing { get; }

    public bool Has(string? id) => id is not null && All.Any(key => string.Equals(key.KeyId, id, StringComparison.Ordinal));

    public static SigningKeys From(IReadOnlyList<SigningKeyOptions> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        if (configured.Count == 0)
        {
            throw new InvalidOperationException(
                "Auth:SigningKeys is empty. Run the app through `doppler run --` or set AUTH__SIGNINGKEYS__0__ID and AUTH__SIGNINGKEYS__0__KEY.");
        }

        var keys = new List<SymmetricSecurityKey>(configured.Count);
        foreach (var (entry, index) in configured.Select((entry, index) => (entry, index)))
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                throw new InvalidOperationException($"Auth:SigningKeys:{index} has no id.");
            }

            if (keys.Any(key => string.Equals(key.KeyId, entry.Id, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Auth:SigningKeys lists the id {entry.Id} twice.");
            }

            var bytes = new byte[entry.Key.Length];
            if (!Convert.TryFromBase64String(entry.Key, bytes, out var length))
            {
                throw new InvalidOperationException($"Auth:SigningKeys key {entry.Id} isn't base64.");
            }

            if (length < MinimumBytes)
            {
                throw new InvalidOperationException(
                    $"Auth:SigningKeys key {entry.Id} is {length} bytes. It needs at least {MinimumBytes}.");
            }

            keys.Add(new SymmetricSecurityKey(bytes[..length]) { KeyId = entry.Id });
        }

        return new SigningKeys(keys);
    }
}
