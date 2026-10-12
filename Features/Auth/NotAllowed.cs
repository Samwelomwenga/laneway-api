namespace Laneway.Api;

public static class NotAllowed
{
    private const string NoAccess = "You don't have access to this.";

    public static ApiResponse<T> Response<T>() =>
        ApiResponse<T>.ErrorResponse(
            NoAccess,
            StatusCodes.Status403Forbidden,
            [new ApiError(null, ErrorCodes.Forbidden, NoAccess)]);
}
