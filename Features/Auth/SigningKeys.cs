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

    public bool Has(string? id) => id is not null && All.Any(key => SameId(key.KeyId, id));

    public static SigningKeys From(IReadOnlyList<SigningKeyOptions> configured)
    {
        var problems = Problems(configured);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        return new SigningKeys(configured
            .Select(entry => new SymmetricSecurityKey(Decode(entry.Key)) { KeyId = entry.Id })
            .ToList());
    }

    public static IReadOnlyList<string> Problems(IReadOnlyList<SigningKeyOptions> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        if (configured.Count == 0)
        {
            return ["Auth:SigningKeys is empty. Run the app through `doppler run --` or set AUTH__SIGNINGKEYS__0__ID and AUTH__SIGNINGKEYS__0__KEY."];
        }

        var problems = new List<string>();
        for (var index = 0; index < configured.Count; index++)
        {
            var entry = configured[index];
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                problems.Add($"Auth:SigningKeys:{index} has no id.");
                continue;
            }

            if (configured.Take(index).Any(earlier => SameId(earlier.Id, entry.Id)))
            {
                problems.Add($"Auth:SigningKeys lists the id {entry.Id} twice.");
            }

            if (Decode(entry.Key) is not { } bytes)
            {
                problems.Add($"Auth:SigningKeys key {entry.Id} isn't base64.");
            }
            else if (bytes.Length < MinimumBytes)
            {
                problems.Add($"Auth:SigningKeys key {entry.Id} is {bytes.Length} bytes. It needs at least {MinimumBytes}.");
            }
        }

        return problems;
    }

    private static bool SameId(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);

    private static byte[]? Decode(string key)
    {
        var bytes = new byte[key.Length];
        return Convert.TryFromBase64String(key, bytes, out var length) ? bytes[..length] : null;
    }
}
