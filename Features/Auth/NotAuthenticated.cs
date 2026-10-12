namespace Laneway.Api;

public static class NotAuthenticated
{
    private const string SignInAgain = "Sign in again.";

    public static ApiResponse<T> Response<T>() =>
        ApiResponse<T>.ErrorResponse(
            SignInAgain,
            StatusCodes.Status401Unauthorized,
            [new ApiError(null, ErrorCodes.NotAuthenticated, SignInAgain)]);
}
