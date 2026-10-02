using System.Text.Json;
using System.Text.Json.Serialization;

namespace Laneway.Api;

public sealed class EnumNameConverter : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _stringEnumConverter = new(allowIntegerValues: false);

    public override bool CanConvert(Type typeToConvert) => _stringEnumConverter.CanConvert(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(SingleNameConverter<>).MakeGenericType(typeToConvert),
            _stringEnumConverter.CreateConverter(typeToConvert, options))!;

    private sealed class SingleNameConverter<TEnum>(JsonConverter<TEnum> stringEnumConverter) : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String
                && !Enum.GetNames<TEnum>().Contains(reader.GetString(), StringComparer.OrdinalIgnoreCase))
            {
                throw new JsonException();
            }

            return stringEnumConverter.Read(ref reader, typeToConvert, options);
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            stringEnumConverter.Write(writer, value, options);
    }
}
