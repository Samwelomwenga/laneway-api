using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class Readable
{
    private readonly ApplicationDbContext _context;
    private readonly DenialLog _denials;

    public Readable(ApplicationDbContext context, DenialLog denials)
    {
        _context = context;
        _denials = denials;
    }

    public IQueryable<WorkSpace> Workspaces(Guid userId) =>
        _context.WorkSpaces.Where(workspace =>
            workspace.Visibility == WorkspaceVisibility.Public
            || _context.WorkspaceMembers.Any(member => member.WorkspaceId == workspace.Id && member.UserId == userId)
            || _context.BoardMembers.Any(member => member.UserId == userId
                && _context.Boards.Any(board => board.Id == member.BoardId && board.WorkspaceId == workspace.Id)));

    public IQueryable<Board> Boards(Guid userId) =>
        _context.Boards.Where(board =>
            board.Visibility == BoardVisibility.Public
            || _context.BoardMembers.Any(member => member.BoardId == board.Id && member.UserId == userId)
            || (board.Visibility == BoardVisibility.Workspace
                && _context.WorkspaceMembers.Any(member =>
                    member.WorkspaceId == board.WorkspaceId && member.UserId == userId)));

    public async Task<Found<WorkSpace, WorkspaceStanding>?> WorkspaceAsync(Guid id, Guid userId)
    {
        var found = await Workspaces(userId)
            .Where(workspace => workspace.Id == id)
            .Select(workspace => new
            {
                Workspace = workspace,
                Role = _context.WorkspaceMembers
                    .Where(member => member.WorkspaceId == workspace.Id && member.UserId == userId)
                    .Select(member => (WorkspaceRole?)member.Role)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return found is null
            ? null
            : new Found<WorkSpace, WorkspaceStanding>(found.Workspace, WorkspaceStanding.Of(found.Role));
    }

    public async Task<Found<Board, BoardStanding>?> BoardAsync(Guid id, Guid userId)
    {
        var found = await Boards(userId)
            .Where(board => board.Id == id)
            .Select(board => new
            {
                Board = board,
                BoardRole = _context.BoardMembers
                    .Where(member => member.BoardId == board.Id && member.UserId == userId)
                    .Select(member => (BoardRole?)member.Role)
                    .FirstOrDefault(),
                WorkspaceRole = _context.WorkspaceMembers
                    .Where(member => member.WorkspaceId == board.WorkspaceId && member.UserId == userId)
                    .Select(member => (WorkspaceRole?)member.Role)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return found is null
            ? null
            : new Found<Board, BoardStanding>(
                found.Board, BoardStanding.Of(found.Board, found.BoardRole, found.WorkspaceRole));
    }

    public ApiResponse<T> NotFound<T>(AccessTarget target, Guid id, Guid userId)
    {
        _denials.Add(new Denial(userId, target, id, null));
        return ApiResponse<T>.ErrorResponse($"{target} not found", StatusCodes.Status404NotFound);
    }

    public ApiResponse<T> Refused<T>(Refusal refusal, AccessTarget target, Guid id, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        _denials.Add(new Denial(userId, target, id, refusal.Code));
        return ApiResponse<T>.ErrorResponse(
            refusal.Message,
            StatusCodes.Status403Forbidden,
            [new ApiError(null, refusal.Code, refusal.Message)]);
    }
}
