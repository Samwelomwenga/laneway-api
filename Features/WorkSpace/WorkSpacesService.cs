using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace DefaultNamespace;

public interface IWorkSpaceService
{
    Task<ApiResponse<List<WorkSpaceDto>>> GetAllAsync(WorkSpaceSearchDto searchDto);
    Task<ApiResponse<WorkSpaceDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceDto createWorkSpaceDto);
    Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceDto updateWorkSpaceDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class WorkSpaceService : IWorkSpaceService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;

    public WorkSpaceService(ApplicationDbContext context, Actor actor)
    {
        _context = context;
        _actor = actor;
    }

    public async Task<ApiResponse<List<WorkSpaceDto>>> GetAllAsync(WorkSpaceSearchDto searchDto)
    {
        var query = _context.WorkSpaces.AsQueryable();

        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(ws => ws.Name.ToLower().Contains(term.ToLower()) ||
                                      ws.Description.ToLower().Contains(term.ToLower()));
        }
        if (searchDto.IsArchived is { } isArchived)
        {
            query = query.Where(ws => ws.IsArchived == isArchived);
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
        var workSpaceDto = MapToDto(existingWorkSpace,  existingWorkSpace.Boards.Select(b => b.Id).ToList());
        return ApiResponse<WorkSpaceDto>.SuccessResponse(workSpaceDto, "Workspace retrieved successfully", 200);
    }

    public async Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceDto createWorkSpaceDto)
    {
        var newWorkSpace = MapToEntity(createWorkSpaceDto, _actor.Id);
        _context.WorkSpaces.Add(newWorkSpace);
        await _context.SaveChangesAsync();
        return ApiResponse<WorkSpaceDto>.SuccessResponse(MapToDto(newWorkSpace), "Workspace created successfully", 201);
    }

    public async Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceDto updateWorkSpaceDto)
    {
        var existingWorkSpace = await _context.WorkSpaces
            .Include(ws => ws.Boards)
            .FirstOrDefaultAsync(ws => ws.Id == id);
        if (existingWorkSpace == null)
        {
            return ApiResponse<WorkSpaceDto>.ErrorResponse("Workspace not found", 404);
        }

        existingWorkSpace.Name = updateWorkSpaceDto.Name!;
        existingWorkSpace.Description = updateWorkSpaceDto.Description ?? string.Empty;
        existingWorkSpace.Visibility = updateWorkSpaceDto.Visibility!.Value;
        existingWorkSpace.IsArchived = updateWorkSpaceDto.IsArchived!.Value;
        _context.StampChange(existingWorkSpace, _actor);

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

        _context.WorkSpaces.Remove(workSpace);
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
            workSpace.IsArchived,
            workSpace.CreatedAt,
            workSpace.UpdatedAt,
            workSpace.CreatedBy,
            workSpace.UpdatedBy,
            boardIds ?? new List<Guid>()
        );
    }

    private static WorkSpace MapToEntity(CreateWorkSpaceDto createWorkSpaceDto, Guid actorId)
    {
        return new WorkSpace
        {
            Id = Guid.NewGuid(),
            Name = createWorkSpaceDto.Name!,
            Description = createWorkSpaceDto.Description ?? string.Empty,
            Visibility = createWorkSpaceDto.Visibility!.Value,
            IsArchived = createWorkSpaceDto.IsArchived!.Value,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
