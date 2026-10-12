using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/profile")]
public class ProfileController : ApiControllerBase
{
    private readonly IUserService _userService;

    public ProfileController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetProfile()
    {
        var response = await _userService.GetProfileAsync();
        return ToActionResult(response);
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateProfile([FromBody] UpdateProfileDto updateProfileDto)
    {
        var response = await _userService.UpdateProfileAsync(updateProfileDto);
        return ToActionResult(response);
    }
}
