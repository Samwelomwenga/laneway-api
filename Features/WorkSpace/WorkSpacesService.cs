using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Laneway.Api;

public interface IWorkSpaceService
{
    Task<ApiResponse<List<WorkspaceSummaryDto>>> GetAllAsync(WorkSpaceSearchDto searchDto);
    Task<ApiResponse<WorkspaceSummaryDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceWrite write);
    Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceWrite write);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class WorkSpaceService : IWorkSpaceService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly ActivityWriter _activity;
    private readonly Readable _readable;

    public WorkSpaceService(ApplicationDbContext context, Actor actor, ActivityWriter activity, Readable readable)
    {
        _context = context;
        _actor = actor;
        _activity = activity;
        _readable = readable;
    }

    public async Task<ApiResponse<List<WorkspaceSummaryDto>>> GetAllAsync(WorkSpaceSearchDto searchDto)
    {
        var userId = _actor.Id;
        var query = _readable.Workspaces(userId)
            .Select(ws => new WorkspaceRow
            {
                Workspace = ws,
                IsMember = _context.WorkspaceMembers.Any(member => member.WorkspaceId == ws.Id && member.UserId == userId)
            })
            .Where(row => row.IsMember
                || _context.BoardMembers.Any(member => member.UserId == userId
                    && _context.Boards.Any(board => board.Id == member.BoardId && board.WorkspaceId == row.Workspace.Id)));

        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(row => row.Workspace.Name.ToLower().Contains(term.ToLower()) ||
                                       (row.IsMember && row.Workspace.Description.ToLower().Contains(term.ToLower())));
        }
        if (searchDto.Visibility is { } visibility)
        {
            query = query.Where(row => row.IsMember && row.Workspace.Visibility == visibility);
        }

        var (rows, totalCount) = await PagedQuery.ReadAsync(
            query.OrderBy(row => row.Workspace.CreatedAt),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        var workSpaceDtos = rows.ConvertAll(row => MapToDto(row.Workspace, row.IsMember));

        return PagedResponse<WorkspaceSummaryDto>.SuccessResponse(
            workSpaceDtos,
            totalCount,
            pageSize: searchDto.PageSize,
            pageNumber: searchDto.PageNumber,
            message: "Workspaces retrieved successfully"
        );
    }

    public async Task<ApiResponse<WorkspaceSummaryDto>> GetByIdAsync(Guid id)
    {
        if (await _readable.WorkspaceAsync(id, _actor.Id) is not { } found)
        {
            return _readable.NotFound<WorkspaceSummaryDto>(AccessTarget.Workspace, id, _actor.Id);
        }

        var workSpaceDto = MapToDto(found.Place, found.Standing.IsMember);
        return ApiResponse<WorkspaceSummaryDto>.SuccessResponse(workSpaceDto, "Workspace retrieved successfully", 200);
    }

    public async Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceWrite write)
    {
        var newWorkSpace = MapToEntity(write, _actor.Id);
        _context.WorkSpaces.Add(newWorkSpace);
        _context.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = newWorkSpace.Id,
            UserId = _actor.Id,
            Role = WorkspaceRole.Admin,
            CreatedAt = newWorkSpace.CreatedAt,
            CreatedBy = _actor.Id
        });
        await _activity.AddAsync(
            ActivityType.CreateWorkspace,
            ActivityPlace.OnWorkspace(newWorkSpace.Id),
            actor => new CreateWorkspaceData(actor, WorkspaceRef.Of(newWorkSpace)));
        await _context.SaveChangesAsync();
        return ApiResponse<WorkSpaceDto>.SuccessResponse(MapToDto(newWorkSpace), "Workspace created successfully", 201);
    }

    public async Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceWrite write)
    {
        if (await _readable.WorkspaceAsync(id, _actor.Id) is not { } found)
        {
            return _readable.NotFound<WorkSpaceDto>(AccessTarget.Workspace, id, _actor.Id);
        }

        if (WorkspaceRules.Refusal(found.Standing, WorkspaceAction.Administer) is { } refusal)
        {
            return _readable.Refused<WorkSpaceDto>(refusal, AccessTarget.Workspace, id, _actor.Id);
        }

        var existingWorkSpace = found.Place;

        existingWorkSpace.Name = write.Name;
        existingWorkSpace.Description = write.Description;
        existingWorkSpace.Visibility = write.Visibility;
        _context.StampChange(existingWorkSpace, _actor);

        var tracked = _context.Entry(existingWorkSpace);
        if (tracked.Changed())
        {
            var old = WorkspaceFields.Changed(tracked);
            await _activity.AddAsync(
                ActivityType.UpdateWorkspace,
                ActivityPlace.OnWorkspace(existingWorkSpace.Id),
                actor => new UpdateWorkspaceData(actor, WorkspaceRef.Of(existingWorkSpace), old));
        }

        await _context.SaveChangesAsync();

        return ApiResponse<WorkSpaceDto>.SuccessResponse(MapToDto(existingWorkSpace), "Workspace updated successfully", 200);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        if (await _readable.WorkspaceAsync(id, _actor.Id) is not { } found)
        {
            return _readable.NotFound<bool>(AccessTarget.Workspace, id, _actor.Id);
        }

        if (WorkspaceRules.Refusal(found.Standing, WorkspaceAction.Administer) is { } refusal)
        {
            return _readable.Refused<bool>(refusal, AccessTarget.Workspace, id, _actor.Id);
        }

        var workSpace = found.Place;

        var boardCount = await _context.Boards.CountAsync(b => b.WorkspaceId == id);
        if (boardCount > 0)
        {
            return ApiResponse<bool>.ErrorResponse("Workspace still holds boards", 409,
                [new ApiError(null, ErrorCodes.NotEmpty,
                    $"Move or delete this workspace's {boardCount} {(boardCount == 1 ? "board" : "boards")} first.")]);
        }

        _context.WorkSpaces.Remove(workSpace);
        await _activity.AddAsync(
            ActivityType.DeleteWorkspace,
            ActivityPlace.OnWorkspace(workSpace.Id),
            actor => new DeleteWorkspaceData(actor, WorkspaceRef.Of(workSpace)));
        await _context.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Workspace deleted successfully", 204);
    }

    private sealed class WorkspaceRow
    {
        public required WorkSpace Workspace { get; init; }
        public required bool IsMember { get; init; }
    }

    private static WorkspaceSummaryDto MapToDto(WorkSpace workSpace, bool isMember) =>
        isMember ? MapToDto(workSpace) : new WorkspaceSummaryDto(workSpace.Id, workSpace.Name);

    private static WorkSpaceDto MapToDto(WorkSpace workSpace)
    {
        return new WorkSpaceDto
        (
            workSpace.Id,
            workSpace.Name,
            workSpace.Description,
            workSpace.Visibility,
            workSpace.CreatedAt,
            workSpace.UpdatedAt,
            workSpace.CreatedBy,
            workSpace.UpdatedBy
        );
    }

    private static WorkSpace MapToEntity(CreateWorkSpaceWrite write, Guid actorId)
    {
        return new WorkSpace
        {
            Id = Guid.NewGuid(),
            Name = write.Name,
            Description = write.Description,
            Visibility = write.Visibility,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
