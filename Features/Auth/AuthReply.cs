namespace Laneway.Api;

public sealed record AuthReply<T>(ApiResponse<T> Response, RefreshCookieChange Cookie, TimeSpan? RetryAfter = null);

public static class AuthReply
{
    public static AuthReply<T> Of<T>(ApiResponse<T> response) => new(response, RefreshCookieChange.None);
}
