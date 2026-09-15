using System.Text.Json;
using System.Text.Json.Serialization;

namespace DefaultNamespace;

public static class JsonSettings
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.Converters.Add(new EnumNameConverter());
        options.Converters.Add(new UtcDateTimeConverter());
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    }
}
