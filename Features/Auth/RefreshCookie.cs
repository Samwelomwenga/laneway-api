namespace Laneway.Api;

public static class RefreshCookie
{
    public const string Name = "__Host-Http-lnw_refresh";

    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var value = request.Cookies[Name];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static void Write(HttpResponse response, RefreshCookieChange change)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(change);

        if (change.Value is { } value)
        {
            response.Cookies.Append(Name, value, Options(change.MaxAge));
        }
    }

    private static CookieOptions Options(TimeSpan maxAge) => new()
    {
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge < TimeSpan.Zero ? TimeSpan.Zero : maxAge
    };
}

public sealed class RefreshCookieChange
{
    private RefreshCookieChange(string? value, TimeSpan maxAge)
    {
        Value = value;
        MaxAge = maxAge;
    }

    public static RefreshCookieChange None { get; } = new(null, TimeSpan.Zero);

    public static RefreshCookieChange Cleared { get; } = new(string.Empty, TimeSpan.Zero);

    public string? Value { get; }

    public TimeSpan MaxAge { get; }

    public static RefreshCookieChange To(string value, TimeSpan maxAge) => new(value, maxAge);
}
