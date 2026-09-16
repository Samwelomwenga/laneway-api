using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IListService
{
    Task<ApiResponse<ListDto>> CreateAsync(CreateListDto createListDto);
    Task<ApiResponse<ListDto>> UpdateAsync(Guid id, UpdateListDto updateListDto);
    Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedDto archivedDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
    Task<ApiResponse<ListDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<List<ListDto>>> GetAllAsync(ListSearchDto searchDto);
}

public class ListService : IListService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly Placements _placements;

    public ListService(ApplicationDbContext context, Actor actor, Placements placements)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
    }

    public async Task<ApiResponse<ListDto>> CreateAsync(CreateListDto createListDto)
    {
        var boardId = createListDto.BoardId!.Value;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.Boards.AnyAsync(b => b.Id == boardId))
        {
            return BoardNotFound(boardId);
        }

        var placed = await _placements.ResolveOnBoardAsync(
            boardId, new Placement(createListDto.Position, createListDto.Before, createListDto.After));
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<ListDto>(placed.Errors);
        }

        var list = MapToEntity(createListDto, _actor.Id, placed.Position);

        _context.Lists.Add(list);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
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

        var boardId = updateListDto.BoardId!.Value;
        if (!await _context.Boards.AnyAsync(b => b.Id == boardId))
        {
            return BoardNotFound(boardId);
        }

        list.Name = updateListDto.Name!;
        list.Position = updateListDto.Position!.Value;
        list.BoardId = boardId;
        list.Color = updateListDto.Color;
        _context.StampChange(list, _actor);

        await _context.SaveChangesAsync();
        var listDto = MapToDto(list, list.Cards.InSortOrder().Select(c => c.Id).ToList());

        return ApiResponse<ListDto>.SuccessResponse(listDto, "List updated successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedDto archivedDto) =>
        await _context.SetArchivedAsync(await _context.Lists.FindAsync(id), _actor, archivedDto, "List");

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var list = await _context.Lists.FindAsync(id);
        if (list == null)
        {
            return ApiResponse<bool>.ErrorResponse("List not found", 404);
        }

        if (!list.IsArchived)
        {
            return ArchiveErrors.NotArchived<bool>("List");
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

        var listDto = MapToDto(list, list.Cards.InSortOrder().Select(c => c.Id).ToList());
        return ApiResponse<ListDto>.SuccessResponse(listDto, "List retrieved successfully", 200);
    }

    public async Task<ApiResponse<List<ListDto>>> GetAllAsync(ListSearchDto searchDto)
    {
        if (searchDto.BoardId is { } filterBoardId && !await _context.Boards.AnyAsync(b => b.Id == filterBoardId))
        {
            return ReferenceErrors.NotFound<List<ListDto>>("boardId", "Board", filterBoardId);
        }

        var query = _context.Lists.AsQueryable();

        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(l => l.Name.ToLower().Contains(term.ToLower()));
        }

        if (searchDto.BoardId is { } boardId)
        {
            query = query.Where(l => l.BoardId == boardId);
        }

        query = ArchiveView.Lists(query, searchDto.Archived, _context);

        var (lists, totalCount) = await PagedQuery.ReadAsync(
            query.InSortOrder().Include(l => l.Cards),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        var listDtos = lists.Select(l => MapToDto(l, l.Cards.InSortOrder().Select(c => c.Id).ToList())).ToList();

        return PagedResponse<ListDto>.SuccessResponse(
            listDtos,
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Lists retrieved successfully"
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

    private static ApiResponse<ListDto> BoardNotFound(Guid boardId) =>
        ApiResponse<ListDto>.ErrorResponse("A referenced resource does not exist", 400,
            [new ApiError("boardId", ErrorCodes.NotFound, $"Board {boardId} does not exist.")]);

    private static List MapToEntity(CreateListDto createListDto, Guid actorId, double position)
    {
        return new List
        {
            Id = Guid.NewGuid(),
            Name = createListDto.Name!,
            Position = position,
            BoardId = createListDto.BoardId!.Value,
            Color = createListDto.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
