using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
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
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }

    [HttpGet("username-exists")]
    public async Task<ActionResult<ApiResponse<bool>>> CheckUserNameExists([FromQuery][Required] string username)
    {
        if(!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<bool>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _userService.UserNameExistsAsync(username);
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser([FromBody] CreateUserDto createUserDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<UserDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _userService.CreateAsync(createUserDto);
        if (!response.Success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetUser), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUser(Guid id, [FromBody] UpdateUserDto updateUserDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<UserDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _userService.UpdateAsync(id, updateUserDto);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteUser(Guid id)
    {
        var response = await _userService.DeleteAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
}
