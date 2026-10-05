namespace Laneway.Api;

public sealed class MissingWriteFieldException : Exception
{
    public MissingWriteFieldException(string field)
        : base($"'{field}' was null after validation.") => Field = field;

    public string Field { get; }
}

public static class Writes
{
    public static T Required<T>(T? value, string field) where T : class =>
        value ?? throw new MissingWriteFieldException(field);

    public static T Required<T>(T? value, string field) where T : struct =>
        value ?? throw new MissingWriteFieldException(field);

    public static List<T>? EachRequired<T>(List<T?>? values, string field) where T : struct =>
        values?.Select((value, index) => Required(value, $"{field}[{index}]")).ToList();
}
