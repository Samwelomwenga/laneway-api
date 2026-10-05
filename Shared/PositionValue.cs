using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DefaultNamespace;

public enum PositionEdge
{
    Top,
    Bottom
}

[JsonConverter(typeof(PositionValueConverter))]
public sealed class PositionValue
{
    private PositionValue(PositionEdge? edge, double? number, string? errorCode)
    {
        Edge = edge;
        Number = number;
        ErrorCode = errorCode;
    }

    public PositionEdge? Edge { get; }
    public double? Number { get; }
    public string? ErrorCode { get; }

    public static PositionValue Top { get; } = new(PositionEdge.Top, null, null);
    public static PositionValue Bottom { get; } = new(PositionEdge.Bottom, null, null);
    public static PositionValue UnknownName { get; } = new(null, null, ErrorCodes.UnknownValue);
    public static PositionValue OutOfRange { get; } = new(null, null, ErrorCodes.OutOfRange);

    public static PositionValue At(double number) =>
        number > 0 ? new PositionValue(null, number, null) : OutOfRange;

    public static PositionValue Named(string name)
    {
        if (string.Equals(name, nameof(PositionEdge.Top), StringComparison.OrdinalIgnoreCase))
        {
            return Top;
        }

        if (string.Equals(name, nameof(PositionEdge.Bottom), StringComparison.OrdinalIgnoreCase))
        {
            return Bottom;
        }

        return double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
               && double.IsFinite(number)
            ? At(number)
            : UnknownName;
    }
}

public sealed class PositionValueConverter : JsonConverter<PositionValue>
{
    public override PositionValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => PositionValue.Named(reader.GetString()!),
            JsonTokenType.Number when reader.TryGetDouble(out var number) => PositionValue.At(number),
            _ => throw new JsonException()
        };

    public override void Write(Utf8JsonWriter writer, PositionValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Number is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else if (value.Edge is { } edge)
        {
            writer.WriteStringValue(edge.ToString());
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
