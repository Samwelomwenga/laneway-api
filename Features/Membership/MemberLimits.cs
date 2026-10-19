using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed record MemberChange(bool WasAdmin, bool Removing, bool StaysAdmin = false)
{
    public static MemberChange Removal(bool wasAdmin) => new(wasAdmin, Removing: true);

    public static MemberChange NewRole(bool wasAdmin, bool staysAdmin) => new(wasAdmin, Removing: false, staysAdmin);
}

public sealed record OtherMembers(int Count, int Admins)
{
    public string? Blocks(MemberChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Removing && Count == 0)
        {
            return ErrorCodes.LastMember;
        }

        return change.WasAdmin && !change.StaysAdmin && Admins == 0 ? ErrorCodes.LastAdmin : null;
    }
}

public static class MemberLimits
{
    public const string LastAdminMessage = "Make another member an admin first.";

    public static Task<OtherMembers> OtherBoardMembersAsync(this ApplicationDbContext context, Guid boardId, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var others = context.BoardMembers.Where(member => member.BoardId == boardId && member.UserId != userId);
        return context.Boards
            .Where(board => board.Id == boardId)
            .Select(_ => new OtherMembers(others.Count(), others.Count(member => member.Role == BoardRole.Admin)))
            .SingleAsync();
    }

    public static Task<OtherMembers> OtherWorkspaceMembersAsync(
        this ApplicationDbContext context, Guid workspaceId, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var others = context.WorkspaceMembers.Where(member => member.WorkspaceId == workspaceId && member.UserId != userId);
        return context.WorkSpaces
            .Where(workspace => workspace.Id == workspaceId)
            .Select(_ => new OtherMembers(others.Count(), others.Count(member => member.Role == WorkspaceRole.Admin)))
            .SingleAsync();
    }

    public static ApiResponse<T> Blocked<T>(string code, string place) =>
        Conflict<T>([new ApiError(null, code, code == ErrorCodes.LastAdmin ? LastAdminMessage : $"Delete this {place} instead.")]);

    public static ApiError OnBoard(string code, string boardName) => new(
        "boards",
        code,
        code == ErrorCodes.LastAdmin
            ? $"Make another member of the board '{boardName}' an admin first."
            : $"The board '{boardName}' has no other members. Delete it instead.");

    public static ApiResponse<T> Conflict<T>(List<ApiError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var message = errors.TrueForAll(error => error.Code == ErrorCodes.LastAdmin)
            ? "A place would be left without an admin"
            : "A place would be left without members";
        return ApiResponse<T>.ErrorResponse(message, StatusCodes.Status409Conflict, errors);
    }

    public static async Task<List<ApiError>> SoleAdminPlacesAsync(this ApplicationDbContext context, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var workspaces = await context.WorkspaceMembers
            .Where(member => member.UserId == userId && member.Role == WorkspaceRole.Admin)
            .Join(context.WorkSpaces, member => member.WorkspaceId, workspace => workspace.Id, (_, workspace) => workspace)
            .Where(workspace =>
                context.WorkspaceMembers.Any(other => other.WorkspaceId == workspace.Id && other.UserId != userId)
                && !context.WorkspaceMembers.Any(other => other.WorkspaceId == workspace.Id
                    && other.UserId != userId && other.Role == WorkspaceRole.Admin))
            .OrderBy(workspace => workspace.Name)
            .Select(workspace => workspace.Name)
            .ToListAsync();
        var boards = await context.BoardMembers
            .Where(member => member.UserId == userId && member.Role == BoardRole.Admin)
            .Join(context.Boards, member => member.BoardId, board => board.Id, (_, board) => board)
            .Where(board =>
                context.BoardMembers.Any(other => other.BoardId == board.Id && other.UserId != userId)
                && !context.BoardMembers.Any(other => other.BoardId == board.Id
                    && other.UserId != userId && other.Role == BoardRole.Admin))
            .OrderBy(board => board.Name)
            .Select(board => board.Name)
            .ToListAsync();

        return
        [
            .. workspaces.Select(name => new ApiError(
                "workspaces", ErrorCodes.LastAdmin, $"Make another member of the workspace '{name}' an admin first.")),
            .. boards.Select(name => OnBoard(ErrorCodes.LastAdmin, name))
        ];
    }
}
