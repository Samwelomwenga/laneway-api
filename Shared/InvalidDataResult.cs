using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

public static class InvalidDataResult
{
    public static BadRequestObjectResult Create(List<ApiError> errors) =>
        new(ApiResponse<object>.ErrorResponse("Invalid data", StatusCodes.Status400BadRequest, errors));
}
