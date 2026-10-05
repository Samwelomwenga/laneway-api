using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult ToActionResult<T>(ApiResponse<T> response)
    {
        return response.StatusCode == StatusCodes.Status204NoContent
            ? NoContent()
            : StatusCode(response.StatusCode, response);
    }
}
