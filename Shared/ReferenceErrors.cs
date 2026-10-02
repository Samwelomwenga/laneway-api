namespace Laneway.Api;

public static class ReferenceErrors
{
    public static ApiResponse<T> NotFound<T>(string field, string resource, Guid id) =>
        ApiResponse<T>.ErrorResponse("A referenced resource does not exist", 400,
            [new ApiError(field, ErrorCodes.NotFound, $"{resource} {id} does not exist.")]);

    public static ApiResponse<T> Invalid<T>(List<ApiError> errors) =>
        ApiResponse<T>.ErrorResponse(
            errors.TrueForAll(error => error.Code == ErrorCodes.NotFound)
                ? "A referenced resource does not exist"
                : "Invalid data",
            400, errors);
}
