using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ApiControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserSummaryDto>>> GetUser(Guid id)
    {
        var response = await _userService.GetSummaryAsync(id);
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteUser(Guid id)
    {
        var response = await _userService.DeleteAsync(id);
        return ToActionResult(response);
    }
}
