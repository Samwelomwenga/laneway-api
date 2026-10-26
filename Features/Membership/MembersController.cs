using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
public class MembersController : ApiControllerBase
{
    private readonly IWorkspaceMembersService _workspaceMembers;
    private readonly IBoardMembersService _boardMembers;

    public MembersController(IWorkspaceMembersService workspaceMembers, IBoardMembersService boardMembers)
    {
        _workspaceMembers = workspaceMembers;
        _boardMembers = boardMembers;
    }

    [HttpGet("/api/v1/workspaces/{workspaceId}/members")]
    public async Task<ActionResult<PagedResponse<WorkspaceMemberDto>>> GetWorkspaceMembers(
        Guid workspaceId, [FromQuery] MemberSearchDto searchDto)
    {
        return ToActionResult(await _workspaceMembers.GetAllAsync(workspaceId, searchDto));
    }

    [HttpPost("/api/v1/workspaces/{workspaceId}/members")]
    [RateLimited(RateLimits.AddMember)]
    public async Task<ActionResult<ApiResponse<AddedWorkspaceMemberDto>>> AddWorkspaceMember(
        Guid workspaceId, [FromBody] AddWorkspaceMemberDto addWorkspaceMemberDto)
    {
        return ToActionResult(await _workspaceMembers.AddAsync(workspaceId, MemberWrites.Of(addWorkspaceMemberDto)));
    }

    [HttpPut("/api/v1/workspaces/{workspaceId}/members/{userId}")]
    public async Task<ActionResult<ApiResponse<WorkspaceMemberDto>>> UpdateWorkspaceMember(
        Guid workspaceId, Guid userId, [FromBody] UpdateWorkspaceMemberDto updateWorkspaceMemberDto)
    {
        return ToActionResult(
            await _workspaceMembers.UpdateAsync(workspaceId, userId, MemberWrites.Of(updateWorkspaceMemberDto)));
    }

    [HttpDelete("/api/v1/workspaces/{workspaceId}/members/{userId}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveWorkspaceMember(Guid workspaceId, Guid userId)
    {
        return ToActionResult(await _workspaceMembers.RemoveAsync(workspaceId, userId));
    }

    [HttpGet("/api/v1/boards/{boardId}/members")]
    public async Task<ActionResult<PagedResponse<BoardMemberDto>>> GetBoardMembers(
        Guid boardId, [FromQuery] MemberSearchDto searchDto)
    {
        return ToActionResult(await _boardMembers.GetAllAsync(boardId, searchDto));
    }

    [HttpPost("/api/v1/boards/{boardId}/members")]
    [RateLimited(RateLimits.AddMember)]
    public async Task<ActionResult<ApiResponse<AddedBoardMemberDto>>> AddBoardMember(
        Guid boardId, [FromBody] AddBoardMemberDto addBoardMemberDto)
    {
        return ToActionResult(await _boardMembers.AddAsync(boardId, MemberWrites.Of(addBoardMemberDto)));
    }

    [HttpPut("/api/v1/boards/{boardId}/members/{userId}")]
    public async Task<ActionResult<ApiResponse<BoardMemberDto>>> UpdateBoardMember(
        Guid boardId, Guid userId, [FromBody] UpdateBoardMemberDto updateBoardMemberDto)
    {
        return ToActionResult(await _boardMembers.UpdateAsync(boardId, userId, MemberWrites.Of(updateBoardMemberDto)));
    }

    [HttpDelete("/api/v1/boards/{boardId}/members/{userId}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveBoardMember(Guid boardId, Guid userId)
    {
        return ToActionResult(await _boardMembers.RemoveAsync(boardId, userId));
    }
}
