using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
public class InvitesController : ApiControllerBase
{
    private readonly IWorkspaceMembersService _workspaceMembers;
    private readonly IBoardMembersService _boardMembers;
    private readonly InviteUnsubscribes _unsubscribes;

    public InvitesController(
        IWorkspaceMembersService workspaceMembers, IBoardMembersService boardMembers, InviteUnsubscribes unsubscribes)
    {
        _workspaceMembers = workspaceMembers;
        _boardMembers = boardMembers;
        _unsubscribes = unsubscribes;
    }

    [HttpGet("/api/v1/workspaces/{workspaceId}/invites")]
    public async Task<ActionResult<PagedResponse<WorkspaceInviteDto>>> GetWorkspaceInvites(
        Guid workspaceId, [FromQuery] MemberSearchDto searchDto)
    {
        return ToActionResult(await _workspaceMembers.GetInvitesAsync(workspaceId, searchDto));
    }

    [HttpDelete("/api/v1/workspaces/{workspaceId}/invites/{inviteId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeWorkspaceInvite(Guid workspaceId, Guid inviteId)
    {
        return ToActionResult(await _workspaceMembers.RevokeInviteAsync(workspaceId, inviteId));
    }

    [HttpGet("/api/v1/boards/{boardId}/invites")]
    public async Task<ActionResult<PagedResponse<BoardInviteDto>>> GetBoardInvites(
        Guid boardId, [FromQuery] MemberSearchDto searchDto)
    {
        return ToActionResult(await _boardMembers.GetInvitesAsync(boardId, searchDto));
    }

    [HttpDelete("/api/v1/boards/{boardId}/invites/{inviteId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeBoardInvite(Guid boardId, Guid inviteId)
    {
        return ToActionResult(await _boardMembers.RevokeInviteAsync(boardId, inviteId));
    }

    [HttpPost("/api/v1/invites/unsubscribe")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<bool>>> Unsubscribe([FromBody] UnsubscribeInvitesDto unsubscribeDto)
    {
        ArgumentNullException.ThrowIfNull(unsubscribeDto);

        return ToActionResult(await _unsubscribes.UnsubscribeAsync(
            Writes.Required(unsubscribeDto.Token, "token"), HttpContext.RequestAborted));
    }
}
