using System.Diagnostics.CodeAnalysis;

namespace Laneway.Api;

public static class Emails
{
    [return: NotNullIfNotNull(nameof(email))]
    public static string? Normalize(string? email) => email?.Trim().ToLowerInvariant();
}
