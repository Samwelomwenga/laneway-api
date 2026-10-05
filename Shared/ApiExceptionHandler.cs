using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DefaultNamespace;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var response = exception switch
        {
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } } =>
                ApiResponse<object>.ErrorResponse("A referenced resource does not exist", StatusCodes.Status400BadRequest,
                    [new ApiError(null, ErrorCodes.NotFound, "A referenced resource does not exist.")]),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                ApiResponse<object>.ErrorResponse("The resource already exists", StatusCodes.Status409Conflict,
                    [new ApiError(null, ErrorCodes.Duplicate, "The resource already exists.")]),
            _ => ApiResponse<object>.ErrorResponse("An unexpected error occurred", StatusCodes.Status500InternalServerError),
        };

        httpContext.Response.StatusCode = response.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
        return true;
    }
}
