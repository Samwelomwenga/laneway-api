using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Laneway.Api;

public interface IWorkSpaceService
{
    Task<ApiResponse<List<WorkSpaceDto>>> GetAllAsync(WorkSpaceSearchDto searchDto);
    Task<ApiResponse<WorkSpaceDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceWrite write);
    Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceWrite write);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class WorkSpaceService : IWorkSpaceService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly ActivityWriter _activity;

    public WorkSpaceService(ApplicationDbContext context, Actor actor, ActivityWriter activity)
    {
        _context = context;
        _actor = actor;
        _activity = activity;
    }

    public async Task<ApiResponse<List<WorkSpaceDto>>> GetAllAsync(WorkSpaceSearchDto searchDto)
    {
        var query = _context.WorkSpaces.AsQueryable();

        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(ws => ws.Name.ToLower().Contains(term.ToLower()) ||
                                      ws.Description.ToLower().Contains(term.ToLower()));
        }
        if (searchDto.Visibility is { } visibility)
        {
            query = query.Where(ws => ws.Visibility == visibility);
        }

        var (workSpaces, totalCount) = await PagedQuery.ReadAsync(
            query.OrderBy(ws => ws.CreatedAt).Include(ws => ws.Boards),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        var workSpaceDtos = workSpaces.Select(ws => MapToDto(ws, ws.Boards.Select(b => b.Id).ToList())).ToList();

        return PagedResponse<WorkSpaceDto>.SuccessResponse(
            workSpaceDtos,
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Workspaces retrieved successfully"
        );
    }

    public async Task<ApiResponse<WorkSpaceDto>> GetByIdAsync(Guid id)
    {
        var existingWorkSpace = await _context.WorkSpaces
            .Where(ws => ws.Id == id)
            .Include(ws => ws.Boards)
            .FirstOrDefaultAsync();
        if (existingWorkSpace == null)
        {
            return ApiResponse<WorkSpaceDto>.ErrorResponse("Workspace not found", 404);
        }
        var workSpaceDto = MapToDto(existingWorkSpace, existingWorkSpace.Boards.Select(b => b.Id).ToList());
        return ApiResponse<WorkSpaceDto>.SuccessResponse(workSpaceDto, "Workspace retrieved successfully", 200);
    }

    public async Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceWrite write)
    {
        var newWorkSpace = MapToEntity(write, _actor.Id);
        _context.WorkSpaces.Add(newWorkSpace);
        await _activity.AddAsync(
            ActivityType.CreateWorkspace,
            ActivityPlace.OnWorkspace(newWorkSpace.Id),
            actor => new CreateWorkspaceData(actor, WorkspaceRef.Of(newWorkSpace)));
        await _context.SaveChangesAsync();
        return ApiResponse<WorkSpaceDto>.SuccessResponse(MapToDto(newWorkSpace), "Workspace created successfully", 201);
    }

    public async Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceWrite write)
    {
        var existingWorkSpace = await _context.WorkSpaces
            .Include(ws => ws.Boards)
            .FirstOrDefaultAsync(ws => ws.Id == id);
        if (existingWorkSpace == null)
        {
            return ApiResponse<WorkSpaceDto>.ErrorResponse("Workspace not found", 404);
        }

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

        return ApiResponse<WorkSpaceDto>.SuccessResponse(MapToDto(existingWorkSpace, existingWorkSpace.Boards.Select(b => b.Id).ToList()), "Workspace updated successfully", 200);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var workSpace = await _context.WorkSpaces.FindAsync(id);
        if (workSpace == null)
        {
            return ApiResponse<bool>.ErrorResponse("Workspace not found", 404);
        }

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

    private static WorkSpaceDto MapToDto(WorkSpace workSpace, List<Guid>? boardIds = null)
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
            workSpace.UpdatedBy,
            boardIds ?? new List<Guid>()
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
