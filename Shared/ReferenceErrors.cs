namespace DefaultNamespace;

public static class ReferenceErrors
{
    public static ApiResponse<T> NotFound<T>(string field, string resource, Guid id) =>
        ApiResponse<T>.ErrorResponse("A referenced resource does not exist", 400,
            [new ApiError(field, ErrorCodes.NotFound, $"{resource} {id} does not exist.")]);
}
