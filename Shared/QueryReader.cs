using System.Globalization;

namespace Laneway.Api;

public sealed class QueryReader
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    private readonly IQueryCollection _query;
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ApiError> _errors = [];

    public QueryReader(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
    }

    public int PageNumber()
    {
        var number = Number("pageNumber");
        if (number is null)
        {
            return 1;
        }

        if (number < 1)
        {
            Add("pageNumber", ErrorCodes.OutOfRange, "'pageNumber' must be at least 1.");
            return 1;
        }

        return number.Value;
    }

    public int PageSize()
    {
        var size = Number("pageSize");
        if (size is null)
        {
            return DefaultPageSize;
        }

        if (size is < 1 or > MaxPageSize)
        {
            Add("pageSize", ErrorCodes.OutOfRange, $"'pageSize' must be between 1 and {MaxPageSize}.");
            return DefaultPageSize;
        }

        return size.Value;
    }

    public string? Text(string key) => Value(key)?.Trim();

    public Guid? Id(string key)
    {
        if (Value(key) is not { } value)
        {
            return null;
        }

        return Guid.TryParse(value, out var id) ? id : Invalid<Guid>(key);
    }

    public bool? Flag(string key)
    {
        if (Value(key) is not { } value)
        {
            return null;
        }

        return bool.TryParse(value, out var flag) ? flag : Invalid<bool>(key);
    }

    public TEnum? EnumName<TEnum>(string key) where TEnum : struct, Enum
    {
        if (Value(key) is not { } value)
        {
            return null;
        }

        var names = Enum.GetNames<TEnum>();
        var match = names.FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Add(key, ErrorCodes.UnknownValue, $"'{key}' must be one of: {string.Join(", ", names)}.");
            return null;
        }

        return Enum.Parse<TEnum>(match);
    }

    public List<TEnum> EnumNames<TEnum>(string key) where TEnum : struct, Enum
    {
        if (Value(key) is not { } value)
        {
            return [];
        }

        var names = Enum.GetNames<TEnum>();
        var chosen = new List<TEnum>();
        foreach (var part in value.Split(',').Select(part => part.Trim()))
        {
            if (part.Length == 0)
            {
                Add(key, ErrorCodes.InvalidFormat, $"'{key}' has an empty entry.");
                continue;
            }

            var match = names.FirstOrDefault(name => string.Equals(name, part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                Add(key, ErrorCodes.UnknownValue,
                    $"'{key}' doesn't take '{part}'. It takes one or more of: {string.Join(", ", names)}.");
                continue;
            }

            chosen.Add(Enum.Parse<TEnum>(match));
        }

        return chosen;
    }

    public DateOnly? Day(string key)
    {
        if (Value(key) is not { } value)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        Add(key, ErrorCodes.InvalidFormat, $"'{key}' must be a date in yyyy-MM-dd format.");
        return null;
    }

    public DateTimeOffset? Timestamp(string key)
    {
        if (Value(key) is not { } value)
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var withKind)
            && withKind.Kind != DateTimeKind.Unspecified
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return moment;
        }

        Add(key, ErrorCodes.InvalidFormat,
            $"'{key}' must be a timestamp with an offset. Encode '+' as '%2B', or send the time in UTC with 'Z'.");
        return null;
    }

    public List<ApiError> Errors() =>
    [
        .. _errors,
        .. _query.Keys
            .Where(key => !_read.Contains(key))
            .Select(key => new ApiError(key, ErrorCodes.UnknownField, $"'{key}' isn't a field on this request."))
    ];

    private int? Number(string key)
    {
        if (Value(key) is not { } value)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : Invalid<int>(key);
    }

    private string? Value(string key)
    {
        _read.Add(key);
        if (!_query.TryGetValue(key, out var values))
        {
            return null;
        }

        if (values.Count > 1)
        {
            Add(key, ErrorCodes.InvalidFormat, $"'{key}' was sent more than once.");
            return null;
        }

        var value = values[0];
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(key, ErrorCodes.InvalidFormat, $"'{key}' can't be empty.");
            return null;
        }

        return value;
    }

    private T? Invalid<T>(string key) where T : struct
    {
        Add(key, ErrorCodes.InvalidFormat, $"'{key}' isn't in a valid format.");
        return null;
    }

    private void Add(string key, string code, string message) => _errors.Add(new ApiError(key, code, message));
}
