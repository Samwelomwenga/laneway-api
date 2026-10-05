using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

public static class InvalidDataResult
{
    public static BadRequestObjectResult Create(List<ApiError> errors) =>
        new(ApiResponse<object>.ErrorResponse("Invalid data", StatusCodes.Status400BadRequest, errors));
}
