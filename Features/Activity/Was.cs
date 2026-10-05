using System.Text.Json;
using System.Text.Json.Serialization;

namespace Laneway.Api;

public sealed record Was<T>(T? Value);

public sealed class WasConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert?.IsGenericType == true && typeToConvert.GetGenericTypeDefinition() == typeof(Was<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(ValueConverter<>).MakeGenericType(typeToConvert!.GetGenericArguments()[0]))!;

    private sealed class ValueConverter<T> : JsonConverter<Was<T>>
    {
        public override Was<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Was<T> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
