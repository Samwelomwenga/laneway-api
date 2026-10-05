using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ApiControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<UserDto>>> GetUsers([FromQuery] UserSearchDto searchDto)
    {
        var response = await _userService.GetAllAsync(searchDto);
        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(Guid id)
    {
        var response = await _userService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpGet("username-exists")]
    public async Task<ActionResult<ApiResponse<bool>>> CheckUserNameExists([FromQuery][Required] string username)
    {
        var response = await _userService.UserNameExistsAsync(username);
        return Ok(response);
    }

    [HttpPost]
    [NoActor]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser([FromBody] CreateUserDto createUserDto)
    {
        var response = await _userService.CreateAsync(createUserDto);
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetUser), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUser(Guid id, [FromBody] UpdateUserDto updateUserDto)
    {
        var response = await _userService.UpdateAsync(id, updateUserDto);
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteUser(Guid id)
    {
        var response = await _userService.DeleteAsync(id);
        return ToActionResult(response);
    }
}
