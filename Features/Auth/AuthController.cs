using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("sign-up")]
    [AllowAnonymous]
    [NoActor]
    public async Task<ActionResult<ApiResponse<SignedUpDto>>> SignUp([FromBody] SignUpDto signUpDto)
    {
        return Reply(await _authService.SignUpAsync(SignUpWrite.Of(signUpDto), Request.Headers.UserAgent.ToString()));
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [NoActor]
    public async Task<ActionResult<ApiResponse<SignedInDto>>> SignIn([FromBody] SignInDto signInDto)
    {
        return Reply(await _authService.SignInAsync(SignInWrite.Of(signInDto), Request.Headers.UserAgent.ToString()));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [NoActor]
    public async Task<ActionResult<ApiResponse<SignedInDto>>> Refresh()
    {
        return Reply(await _authService.RefreshAsync(RefreshCookie.Read(Request)));
    }

    private ActionResult Reply<T>(AuthReply<T> reply)
    {
        RefreshCookie.Write(Response, reply.Cookie);
        return ToActionResult(reply.Response);
    }
}
