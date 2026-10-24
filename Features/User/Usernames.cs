using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Laneway.Api;

public static partial class Usernames
{
    public const int MinimumLength = 3;

    [return: NotNullIfNotNull(nameof(username))]
    public static string? Normalize(string? username) =>
        username is null
            ? null
            : string.Concat(username.Trim().Select(c => char.IsAsciiLetterUpper(c) ? char.ToLowerInvariant(c) : c));

    public static bool IsValid(string username) =>
        username.Length is >= MinimumLength and <= FieldLimits.Username && Allowed().IsMatch(username);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex Allowed();
}
