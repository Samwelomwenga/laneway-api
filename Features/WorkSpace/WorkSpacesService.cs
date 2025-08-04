using Microsoft.EntityFrameworkCore;

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
        try
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
                .ToListAsync();

            var workSpaceDtos = workSpaces.Select(MapToDto).ToList();

            return PagedResponse<WorkSpaceDto>.SuccessResponse(
                workSpaceDtos,
                totalCount,
                searchDto.PageSize,
                searchDto.PageNumber,
                "Workspaces retrieved successfully"
            );
        }
        catch (Exception e)
        {
            return PagedResponse<WorkSpaceDto>.ErrorResponse("An error occurred while retrieving workspaces", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<WorkSpaceDto>> GetByIdAsync(Guid id)
    {
        try
        {
            var existingWorkSpace = await _context.WorkSpaces
                .FirstOrDefaultAsync(ws => ws.Id == id);
            if (existingWorkSpace == null)
            {
               return ApiResponse<WorkSpaceDto>.ErrorResponse("Workspace not found", 404);
            }
            var workSpaceDto = MapToDto(existingWorkSpace);
            return ApiResponse<WorkSpaceDto>.SuccessResponse(workSpaceDto, "Workspace retrieved successfully", 200);
        }
        catch (Exception e)
        {
            return ApiResponse<WorkSpaceDto>.ErrorResponse("An error occurred while retrieving the workspace", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<WorkSpaceDto>> CreateAsync(CreateWorkSpaceDto createWorkSpaceDto)
    {
        try
        {
            var newWorkSpace = MapToEntity(createWorkSpaceDto);
            _context.WorkSpaces.Add(newWorkSpace);
            await _context.SaveChangesAsync();
            return ApiResponse<WorkSpaceDto>.SuccessResponse(MapToDto(newWorkSpace), "Workspace created successfully", 201);

        }
        catch (Exception e)
        {
           return ApiResponse<WorkSpaceDto>.ErrorResponse("An error occurred while creating the workspace", 500,
                new List<string> { e.Message });
        }
    }
    public async Task<ApiResponse<WorkSpaceDto>> UpdateAsync(Guid id, UpdateWorkSpaceDto updateWorkSpaceDto)
    {
        try
        {
            var existingWorkSpace = await _context.WorkSpaces.FindAsync(id);
            if (existingWorkSpace == null)
            {
                return ApiResponse<WorkSpaceDto>.ErrorResponse("Workspace not found", 404);
            }

            existingWorkSpace.Name = updateWorkSpaceDto.Name;
            existingWorkSpace.Description = updateWorkSpaceDto.Description;
            existingWorkSpace.Visibility = updateWorkSpaceDto.Visibility;
            existingWorkSpace.IsArchived = updateWorkSpaceDto.IsArchived;

            _context.WorkSpaces.Update(existingWorkSpace);
            await _context.SaveChangesAsync();

            return ApiResponse<WorkSpaceDto>.SuccessResponse(MapToDto(existingWorkSpace), "Workspace updated successfully", 200);
        }
        catch (Exception e)
        {
            return ApiResponse<WorkSpaceDto>.ErrorResponse("An error occurred while updating the workspace", 500,
                new List<string> { e.Message });
        }
    }
    
    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        try
        {
            var workSpace = await _context.WorkSpaces.FindAsync(id);
            if (workSpace == null)
            {
                return ApiResponse<bool>.ErrorResponse("Workspace not found", 404);
            }

            _context.WorkSpaces.Remove(workSpace);
            await _context.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Workspace deleted successfully", 200);
        }
        catch (Exception e)
        {
            return ApiResponse<bool>.ErrorResponse("An error occurred while deleting the workspace", 500,
                new List<string> { e.Message });
        }
    }
    
    private static WorkSpaceDto MapToDto(WorkSpace workSpace)
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
            workSpace.UpdatedBy
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
