using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IListService
{
    Task<ApiResponse<ListDto>> CreateAsync(CreateListDto createListDto);
    Task<ApiResponse<ListDto>> UpdateAsync(Guid id, UpdateListDto updateListDto);
    Task<ApiResponse<ListDto>> MoveAsync(Guid id, MoveListDto moveListDto);
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
    private readonly ArchiveGuard _archive;
    private readonly LabelMatching _labelMatching;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;

    public ListService(
        ApplicationDbContext context,
        Actor actor,
        Placements placements,
        ArchiveGuard archive,
        LabelMatching labelMatching,
        ActivityWriter activity,
        ActivityTree tree)
    {
        _context = context;
        _actor = actor;
        _placements = placements;
        _archive = archive;
        _labelMatching = labelMatching;
        _activity = activity;
        _tree = tree;
    }

    public async Task<ApiResponse<ListDto>> CreateAsync(CreateListDto createListDto)
    {
        var boardId = createListDto.BoardId!.Value;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.Boards.AnyAsync(b => b.Id == boardId))
        {
            return BoardNotFound(boardId);
        }

        var placed = await _placements.ResolveOnBoardAsync(boardId, Placement.Of(createListDto));
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<ListDto>(placed.Errors);
        }

        if (await _archive.OnBoardAsync(boardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<ListDto>(archived, "boardId", TreeItem.List);
        }

        var list = MapToEntity(createListDto, _actor.Id, placed.Position);

        _context.Lists.Add(list);
        var chain = await _tree.BoardAsync(boardId);
        await _activity.AddAsync(
            ActivityType.CreateList,
            chain.PlaceOn(list.Id),
            actor => new CreateListData(actor, chain.Workspace, chain.Board, ListRef.Of(list)));
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

        if (await _archive.OnListAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<ListDto>(archived, TreeItem.List);
        }

        list.Name = updateListDto.Name!;
        list.Color = updateListDto.Color;
        _context.StampChange(list, _actor);

        var tracked = _context.Entry(list);
        if (tracked.Changed())
        {
            var old = ListFields.Changed(tracked);
            var chain = await _tree.BoardAsync(list.BoardId);
            await _activity.AddAsync(
                ActivityType.UpdateList,
                chain.PlaceOn(list.Id),
                actor => new UpdateListData(actor, chain.Workspace, chain.Board, ListRef.Of(list), old));
        }

        await _context.SaveChangesAsync();
        var listDto = MapToDto(list, list.Cards.InSortOrder().Select(c => c.Id).ToList());

        return ApiResponse<ListDto>.SuccessResponse(listDto, "List updated successfully", 200);
    }

    public async Task<ApiResponse<ListDto>> MoveAsync(Guid id, MoveListDto moveListDto)
    {
        ArgumentNullException.ThrowIfNull(moveListDto);

        var boardId = moveListDto.BoardId!.Value;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var list = await _context.Lists
            .Include(l => l.Cards)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (list == null)
        {
            return ApiResponse<ListDto>.ErrorResponse("List not found", 404);
        }

        if (await _archive.OnListAsync(id) is { } readOnly)
        {
            return ArchiveErrors.ReadOnly<ListDto>(readOnly, TreeItem.List);
        }

        if (!await _context.Boards.AnyAsync(b => b.Id == boardId))
        {
            return BoardNotFound(boardId);
        }

        var crossesBoards = boardId != list.BoardId;
        var placed = await _placements.ResolveMoveOnBoardAsync(list, boardId, Placement.Of(moveListDto));
        if (placed.Errors.Count > 0)
        {
            return ReferenceErrors.Invalid<ListDto>(placed.Errors);
        }

        if (await _archive.OnBoardAsync(boardId) is { } archived)
        {
            return ArchiveErrors.NoCreate<ListDto>(archived, "boardId", TreeItem.List);
        }

        var createdLabels = new List<LabelRef>();
        if (crossesBoards)
        {
            var cards = await _context.Cards
                .Where(card => card.ListId == id)
                .Include(card => card.Labels)
                .ToListAsync();
            var matches = await _labelMatching.CarryToBoardAsync(cards, boardId);
            createdLabels.AddRange(matches.Where(match => match.Created).Select(match => LabelRef.Of(match.To)));
        }

        var leaving = list.BoardId;
        var wasAt = list.Position;
        list.BoardId = boardId;
        list.Position = placed.Position;
        _context.StampChange(list, _actor);

        if (_context.Entry(list).Changed())
        {
            var to = await _tree.BoardAsync(boardId);
            var origin = crossesBoards ? ListOrigin.Between(await _tree.BoardAsync(leaving), to) : null;
            await _activity.AddAsync(
                ActivityType.MoveList,
                new ActivityPlace(
                    WorkspaceId: to.Workspace.Id,
                    BoardId: to.Board.Id,
                    ListId: list.Id,
                    FromWorkspaceId: origin?.Workspace?.Id,
                    FromBoardId: origin?.Board.Id),
                actor => new MoveListData(
                    actor,
                    to.Workspace,
                    to.Board,
                    ListRef.Of(list),
                    new PositionChange(wasAt, list.Position),
                    origin,
                    createdLabels.Count > 0 ? createdLabels : null));
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        var listDto = MapToDto(list, list.Cards.InSortOrder().Select(c => c.Id).ToList());

        return ApiResponse<ListDto>.SuccessResponse(listDto, "List moved successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedDto archivedDto)
    {
        var list = await _context.Lists.FindAsync(id);
        if (list == null)
        {
            return ApiResponse<bool>.ErrorResponse("List not found", 404);
        }

        if (await _archive.OnBoardAsync(list.BoardId) is { } archived)
        {
            return ArchiveErrors.RestoreFirst<bool>(archived, TreeItem.List);
        }

        return await _context.SetArchivedAsync(list, _actor, archivedDto, "List", async archived =>
        {
            var chain = await _tree.BoardAsync(list.BoardId);
            await _activity.AddAsync(
                archived ? ActivityType.ArchiveList : ActivityType.RestoreList,
                chain.PlaceOn(list.Id),
                actor => new ArchiveListData(actor, chain.Workspace, chain.Board, ListRef.Of(list)));
        });
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var list = await _context.Lists.FindAsync(id);
        if (list == null)
        {
            return ApiResponse<bool>.ErrorResponse("List not found", 404);
        }

        if (!list.IsArchived)
        {
            return ArchiveErrors.NotArchived<bool>(TreeItem.List);
        }

        var chain = await _tree.BoardAsync(list.BoardId);
        _context.Lists.Remove(list);
        await _activity.AddAsync(
            ActivityType.DeleteList,
            chain.PlaceOn(list.Id),
            actor => new DeleteListData(actor, chain.Workspace, chain.Board, ListRef.Of(list)));
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
