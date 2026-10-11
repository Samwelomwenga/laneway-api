using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ApiControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("sign-up")]
    [AllowAnonymous]
    [NoActor]
    public async Task<ActionResult<ApiResponse<SignedUpDto>>> SignUp([FromBody] SignUpDto signUpDto)
    {
        var response = await _authService.SignUpAsync(SignUpWrite.Of(signUpDto), Request.Headers.UserAgent.ToString());
        return ToActionResult(response);
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [NoActor]
    public async Task<ActionResult<ApiResponse<SignedInDto>>> SignIn([FromBody] SignInDto signInDto)
    {
        var response = await _authService.SignInAsync(SignInWrite.Of(signInDto), Request.Headers.UserAgent.ToString());
        return ToActionResult(response);
    }
}
