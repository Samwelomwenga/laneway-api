using System.Text.Json;

namespace Laneway.Api;

public static class MessageBodies
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static JsonDocument Of(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.SerializeToDocument(message, message.GetType(), Options);
    }

    public static TMessage In<TMessage>(JsonDocument stored) where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(stored);
        return stored.Deserialize<TMessage>(Options)!;
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonSettings.Apply(options);

        return options;
    }
}
