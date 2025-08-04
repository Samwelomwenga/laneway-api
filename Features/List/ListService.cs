using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IListService
{
    Task<ApiResponse<ListDto>> CreateAsync(CreateListDto createListDto);
    Task<ApiResponse<ListDto>> UpdateAsync(Guid id, UpdateListDto updateListDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
    Task<ApiResponse<ListDto>> GetByIdAsync(Guid id);
    Task<PagedResponse<ListDto>> GetAllAsync(ListSearchDto searchDto);
}

public class ListService : IListService
{
    private readonly ApplicationDbContext _context;

    public ListService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<ListDto>> CreateAsync(CreateListDto createListDto)
    {
        try
        {
            var list = MapToEntity(createListDto);

            _context.Lists.Add(list);
            await _context.SaveChangesAsync();
            var listDto = MapToDto(list);

            return ApiResponse<ListDto>.SuccessResponse(listDto, "List created successfully", 201);
        }
        catch (Exception e)
        {
            return ApiResponse<ListDto>.ErrorResponse("An error occurred while creating the list", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<ListDto>> UpdateAsync(Guid id, UpdateListDto updateListDto)
    {
        try
        {
            var list = await _context.Lists.FindAsync(id);
            if (list == null)
            {
                return ApiResponse<ListDto>.ErrorResponse("List not found", 404);
            }

            list.Name = updateListDto.Name;
            list.Position = updateListDto.Position;
            list.BoardId = updateListDto.BoardId;
            list.Color = updateListDto.Color;
            list.IsArchived = updateListDto.IsArchived;
            list.UpdatedAt = DateTime.UtcNow;
            list.UpdatedBy = Guid.NewGuid(); // This should be set to the current user's ID

            _context.Lists.Update(list);
            await _context.SaveChangesAsync();
            var listDto = MapToDto(list);

            return ApiResponse<ListDto>.SuccessResponse(listDto, "List updated successfully", 200);
        }
        catch (Exception e)
        {
            return ApiResponse<ListDto>.ErrorResponse("An error occurred while updating the list", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        try
        {
            var list = await _context.Lists.FindAsync(id);
            if (list == null)
            {
                return ApiResponse<bool>.ErrorResponse("List not found", 404);
            }

            _context.Lists.Remove(list);
            await _context.SaveChangesAsync();
                
            return ApiResponse<bool>.SuccessResponse(true, "List deleted successfully", 204);
        }
        catch (Exception e)
        {
            return ApiResponse<bool>.ErrorResponse("An error occurred while deleting the list", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<ListDto>> GetByIdAsync(Guid id)
    {
        try
        {
            var list = await _context.Lists.FindAsync(id);
            if (list == null)
            {
                return ApiResponse<ListDto>.ErrorResponse("List not found", 404);
            }

            var listDto = MapToDto(list);
            return ApiResponse<ListDto>.SuccessResponse(listDto, "List retrieved successfully", 200);
        }
        catch (Exception e)
        {
           return ApiResponse<ListDto>.ErrorResponse("An error occurred while retrieving the list", 500,
                new List<string> { e.Message });
        }
    }
    
    public async Task<PagedResponse<ListDto>> GetAllAsync(ListSearchDto searchDto)
    {
        try
        {
            var query = _context.Lists.AsQueryable();

            if (searchDto.BoardId.HasValue)
            {
                query = query.Where(l => l.BoardId == searchDto.BoardId.Value);
            }

            if (searchDto.IsArchived.HasValue)
            {
                query = query.Where(l => l.IsArchived == searchDto.IsArchived.Value);
            }

            if (!string.IsNullOrEmpty(searchDto.SearchTerm))
            {
                query = query.Where(l => l.Name.Contains(searchDto.SearchTerm));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);
            var lists = await query
                .OrderBy(l => l.Position)
                .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                .Take(searchDto.PageSize)
                .ToListAsync();

            var listDtos = lists.Select(MapToDto).ToList();

            return new PagedResponse<ListDto>
            {
                Data = listDtos,
                TotalCount = totalCount,
                PageSize = searchDto.PageSize,
                CurrentPage = searchDto.PageNumber,
                TotalPages = totalPages
            };
        }
        catch (Exception e)
        {
            return PagedResponse<ListDto>.ErrorResponse("An error occurred while retrieving lists", 500,
                new List<string> { e.Message });
        }
    }
    
    private static ListDto MapToDto(List list)
    {
        return new ListDto(
            list.Id,
            list.Name,
            list.Position,
            list.BoardId,
            list.Color,
            list.IsArchived,
            list.CreatedAt,
            list.UpdatedAt,
            list.CreatedBy,
            list.UpdatedBy
        );
    }
    
    private static List MapToEntity(CreateListDto createListDto)
    {
        return new List
        {
            Id = Guid.NewGuid(),
            Name = createListDto.Name,
            Position = createListDto.Position,
            BoardId = createListDto.BoardId,
            Color = createListDto.Color,
            IsArchived = createListDto.IsArchived,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid() // This should be set to the current user's ID
        };
    }
}