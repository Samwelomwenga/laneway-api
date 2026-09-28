using System.Text.Json;

namespace DefaultNamespace;

public static class ListCopyRequest
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static JsonDocument Of(CopyListDto request) => JsonSerializer.SerializeToDocument(request, Options);

    public static CopyListDto In(JsonDocument stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return stored.Deserialize<CopyListDto>(Options)!;
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonSettings.Apply(options);

        return options;
    }
}
