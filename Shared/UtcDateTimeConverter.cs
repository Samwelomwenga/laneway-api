using System.Text.Json;
using System.Text.Json.Serialization;

namespace DefaultNamespace;

public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !reader.TryGetDateTime(out var parsed)
            || parsed.Kind == DateTimeKind.Unspecified
            || !reader.TryGetDateTimeOffset(out var withOffset))
        {
            throw new JsonException();
        }

        return withOffset.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime());
    }
}
