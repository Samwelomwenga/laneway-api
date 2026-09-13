using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace DefaultNamespace;

public interface IWorkSpaceService
{
    Task<PagedResponse<WorkSpaceDto>> GetAllAsync(WorkSpaceSearchDto searchDto);
    Task<ApiResponse<WorkSpaceDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceDto createWorkSpaceDto);
    Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceDto updateWorkSpaceDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class WorkSpaceService : IWorkSpaceService
{
    private readonly ApplicationDbContext _context;

    public WorkSpaceService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResponse<WorkSpaceDto>> GetAllAsync(WorkSpaceSearchDto searchDto)
    {
        var query = _context.WorkSpaces.AsQueryable();

        if (!string.IsNullOrEmpty(searchDto.SearchTerm))
        {
            query = query.Where(ws => ws.Name.Contains(searchDto.SearchTerm) ||
                                      (ws.Description != null && ws.Description.Contains(searchDto.SearchTerm)));
        }
        if (searchDto.IsArchived.HasValue)
        {
            query = query.Where(ws => ws.IsArchived == searchDto.IsArchived.Value);
        }
        if (!string.IsNullOrEmpty(searchDto.Visibility))
        {
            query = query.Where(ws => ws.Visibility == searchDto.Visibility);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);
        var workSpaces = await query
            .OrderBy(ws => ws.CreatedAt)
            .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
            .Take(searchDto.PageSize)
            .Include(ws => ws.Boards)
            .ToListAsync();

        var workSpaceDtos = workSpaces.Select(ws => MapToDto(ws, ws.Boards.Select(b => b.Id).ToList())).ToList();

        return PagedResponse<WorkSpaceDto>.SuccessResponse(
            workSpaceDtos,
            totalCount,
            searchDto.PageSize,
            searchDto.PageNumber,
            "Workspaces retrieved successfully"
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
        var newWorkSpace = MapToEntity(createWorkSpaceDto);
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

        existingWorkSpace.Name = updateWorkSpaceDto.Name;
        existingWorkSpace.Description = updateWorkSpaceDto.Description;
        existingWorkSpace.Visibility = updateWorkSpaceDto.Visibility;
        existingWorkSpace.IsArchived = updateWorkSpaceDto.IsArchived;
        existingWorkSpace.UpdatedAt = DateTime.UtcNow;

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

    private static WorkSpace MapToEntity(CreateWorkSpaceDto createWorkSpaceDto)
    {
        return new WorkSpace
        {
            Id = Guid.NewGuid(),
            Name = createWorkSpaceDto.Name,
            Description = createWorkSpaceDto.Description,
            Visibility = createWorkSpaceDto.Visibility,
            IsArchived = createWorkSpaceDto.IsArchived,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid() // This should be set to the current user's ID
        };
    }
}
