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
        var list = MapToEntity(createListDto);

        _context.Lists.Add(list);
        await _context.SaveChangesAsync();
        var listDto = MapToDto(list);

        return ApiResponse<ListDto>.SuccessResponse(listDto, "List created successfully", 201);
    }

    public async Task<ApiResponse<ListDto>> UpdateAsync(Guid id, UpdateListDto updateListDto)
    {
        var list = await _context.Lists
            .Include(l => l.Cards)
            .FirstOrDefaultAsync(l => l.Id == id);
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

        await _context.SaveChangesAsync();
        var listDto = MapToDto(list, list.Cards.Select(c => c.Id).ToList());

        return ApiResponse<ListDto>.SuccessResponse(listDto, "List updated successfully", 200);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
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

    public async Task<ApiResponse<ListDto>> GetByIdAsync(Guid id)
    {
        var list = await _context.Lists
            .Include(l => l.Cards)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (list == null)
        {
            return ApiResponse<ListDto>.ErrorResponse("List not found", 404);
        }

        var listDto = MapToDto(list, list.Cards.Select(c => c.Id).ToList());
        return ApiResponse<ListDto>.SuccessResponse(listDto, "List retrieved successfully", 200);
    }

    public async Task<PagedResponse<ListDto>> GetAllAsync(ListSearchDto searchDto)
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
            .Include(l => l.Cards)
            .ToListAsync();

        var listDtos = lists.Select(l => MapToDto(l, l.Cards.Select(c => c.Id).ToList())).ToList();

        return PagedResponse<ListDto>.SuccessResponse(
            listDtos,
            totalCount,
            searchDto.PageSize,
            searchDto.PageNumber,
            "Lists retrieved successfully"
        );
    }

    private static ListDto MapToDto(List list, List<Guid>? cardIds = null)
    {
        return new ListDto(
            list.Id,
            list.Name,
            list.Position,
            list.BoardId,
            list.Color,
            list.IsArchived,
            cardIds ?? new List<Guid>(),
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
