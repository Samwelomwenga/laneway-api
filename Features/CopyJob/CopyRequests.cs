using System.Text.Json;

namespace Laneway.Api;

public static class CopyRequests
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static JsonDocument Of<TRequest>(TRequest request) =>
        JsonSerializer.SerializeToDocument(request, Options);

    public static TRequest In<TRequest>(JsonDocument stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return stored.Deserialize<TRequest>(Options)!;
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonSettings.Apply(options);

        return options;
    }
}
