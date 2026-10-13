namespace Laneway.Api;

public static class CommonPasswords
{
    public const string ResourceName = "Laneway.Api.CommonPasswords.txt";

    private static readonly Lazy<HashSet<string>> Set = new(Load);

    public static bool Contains(string password) => Set.Value.Contains(password);

    private static HashSet<string> Load()
    {
        using var stream = typeof(CommonPasswords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            set.Add(line);
        }

        return set;
    }
}
