using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly Actor _actor;

    public AuthController(IAuthService authService, Actor actor)
    {
        _authService = authService;
        _actor = actor;
    }

    [HttpPost("sign-up")]
    [AllowAnonymous]
    [CsrfChecked]
    public async Task<ActionResult<ApiResponse<SignedUpDto>>> SignUp([FromBody] SignUpDto signUpDto)
    {
        return Reply(await _authService.SignUpAsync(SignUpWrite.Of(signUpDto), Request.Headers.UserAgent.ToString()));
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [CsrfChecked]
    public async Task<ActionResult<ApiResponse<SignedInDto>>> SignIn([FromBody] SignInDto signInDto)
    {
        return Reply(await _authService.SignInAsync(SignInWrite.Of(signInDto), Request.Headers.UserAgent.ToString()));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [CsrfChecked]
    public async Task<ActionResult<ApiResponse<SignedInDto>>> Refresh()
    {
        return Reply(await _authService.RefreshAsync(RefreshCookie.Read(Request)));
    }

    [HttpPost("sign-out")]
    [Authorize]
    public async Task<ActionResult> SignOutOfThisSession()
    {
        return Reply(await _authService.SignOutAsync(_actor.Id, _actor.SessionId));
    }

    [HttpPost("sign-out-everywhere")]
    [Authorize]
    public async Task<ActionResult> SignOutEverywhere()
    {
        return Reply(await _authService.SignOutEverywhereAsync(_actor.Id));
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<SessionDto>>>> ListSessions()
    {
        return ToActionResult(await _authService.ListSessionsAsync(_actor.Id, _actor.SessionId));
    }

    [HttpPost("sessions/{sessionId:guid}/sign-out")]
    [Authorize]
    public async Task<ActionResult> EndSession(Guid sessionId)
    {
        return Reply(await _authService.EndSessionAsync(_actor.Id, _actor.SessionId, sessionId));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordDto changePasswordDto)
    {
        return Reply(await _authService.ChangePasswordAsync(
            _actor.Id, _actor.SessionId, ChangePasswordWrite.Of(changePasswordDto)));
    }

    [HttpPost("change-email")]
    [Authorize]
    public async Task<ActionResult> ChangeEmail([FromBody] ChangeEmailDto changeEmailDto)
    {
        return Reply(await _authService.ChangeEmailAsync(_actor.Id, _actor.SessionId, ChangeEmailWrite.Of(changeEmailDto)));
    }

    [HttpPost("delete-user")]
    [Authorize]
    public async Task<ActionResult> DeleteUser([FromBody] DeleteUserDto deleteUserDto)
    {
        return Reply(await _authService.DeleteUserAsync(_actor.Id, _actor.SessionId, DeleteUserWrite.Of(deleteUserDto)));
    }

    private ActionResult Reply<T>(AuthReply<T> reply)
    {
        RefreshCookie.Write(Response, reply.Cookie);
        if (reply.RetryAfter is { } retryAfter)
        {
            Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return ToActionResult(reply.Response);
    }
}
