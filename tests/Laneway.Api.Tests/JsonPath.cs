using System.Text.Json;

namespace Laneway.Api.Tests;

public static class JsonPath
{
    public static bool Has(this JsonElement element, string path) => Find(element, path) is not null;

    public static string? Text(this JsonElement element, string path) => At(element, path).GetString();

    public static Guid Id(this JsonElement element, string path) => At(element, path).GetGuid();

    public static JsonElement At(this JsonElement element, string path) =>
        Find(element, path) ?? throw new InvalidOperationException($"'{path}' isn't in {element.GetRawText()}.");

    private static JsonElement? Find(JsonElement element, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var found = element;
        foreach (var name in path.Split('.'))
        {
            if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(name, out var next))
            {
                return null;
            }

            found = next;
        }

        return found;
    }
}
