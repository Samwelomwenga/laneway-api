using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IBoardService
{
    Task<ApiResponse<List<BoardDto>>> GetAllAsync(Guid workspaceId, BoardSearchDto searchDto);
    Task<ApiResponse<BoardViewDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardWrite write);
    Task<ApiResponse<BoardDto>> UpdateAsync(Guid id, UpdateBoardWrite write);
    Task<ApiResponse<BoardDto>> MoveAsync(Guid id, MoveBoardWrite write);
    Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class BoardService : IBoardService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;
    private readonly ArchiveGuard _archive;
    private readonly ActivityWriter _activity;
    private readonly ActivityTree _tree;
    private readonly CardReads _cards;

    public BoardService(
        ApplicationDbContext context,
        Actor actor,
        ArchiveGuard archive,
        ActivityWriter activity,
        ActivityTree tree,
        CardReads cards)
    {
        _context = context;
        _actor = actor;
        _archive = archive;
        _activity = activity;
        _tree = tree;
        _cards = cards;
    }

    public async Task<ApiResponse<List<BoardDto>>> GetAllAsync(Guid workspaceId, BoardSearchDto searchDto)
    {
        if (!await _context.WorkSpaces.AnyAsync(ws => ws.Id == workspaceId))
        {
            return ReferenceErrors.ParentNotFound<List<BoardDto>>("Workspace");
        }

        var query = _context.Boards.Where(b => b.WorkspaceId == workspaceId);

        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(b => b.Name.ToLower().Contains(term.ToLower()) ||
                                     b.Description.ToLower().Contains(term.ToLower()));
        }

        query = ArchiveView.Boards(query, searchDto.Archived);

        if (searchDto.Visibility is { } visibility)
        {
            query = query.Where(b => b.Visibility == visibility);
        }

        var (boards, totalCount) = await PagedQuery.ReadAsync(
            query.OrderBy(b => b.CreatedAt),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        var boardDtos = boards.Select(MapToDto).ToList();

        return PagedResponse<BoardDto>.SuccessResponse(
            boardDtos,
            totalCount,
            pageSize: searchDto.PageSize,
            pageNumber: searchDto.PageNumber,
            message: "Boards retrieved successfully"
        );
    }

    public async Task<ApiResponse<BoardViewDto>> GetByIdAsync(Guid id)
    {
        var existingBoard = await _context.Boards.FirstOrDefaultAsync(b => b.Id == id);
        if (existingBoard == null)
        {
            return ApiResponse<BoardViewDto>.ErrorResponse("Board not found", 404);
        }

        var labels = await _context.Labels.Where(l => l.BoardId == id).InNameOrder().ToListAsync();
        var lists = await ArchiveView.Lists(_context.Lists.Where(l => l.BoardId == id), ArchiveFilter.Exclude)
            .InSortOrder()
            .ToListAsync();
        var listIds = lists.ConvertAll(l => l.Id);
        var cards = await _cards.OnBoardAsync(
            ArchiveView.Cards(_context.Cards.Where(c => listIds.Contains(c.ListId)), ArchiveFilter.Exclude)
                .InSortOrder());
        var cardsByList = cards.ToLookup(card => card.ListId);

        var boardView = new BoardViewDto(
            MapToDto(existingBoard),
            labels.ConvertAll(LabelView.Of),
            lists.ConvertAll(list => new BoardListDto(
                ListView.Of(list),
                cardsByList[list.Id].ToList())));
        return ApiResponse<BoardViewDto>.SuccessResponse(boardView, "Board retrieved successfully", 200);
    }

    public async Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardWrite write)
    {
        var workspaceId = write.WorkspaceId;
        if (await _tree.FindWorkspaceAsync(workspaceId) is not { } workspace)
        {
            return ReferenceErrors.ParentNotFound<BoardDto>("Workspace");
        }

        var boardEntity = MapToEntity(write, _actor.Id);
        _context.Boards.Add(boardEntity);
        await _activity.AddAsync(
            ActivityType.CreateBoard,
            new ActivityPlace(WorkspaceId: workspaceId, BoardId: boardEntity.Id),
            actor => new CreateBoardData(actor, workspace, BoardRef.Of(boardEntity)));
        await _context.SaveChangesAsync();
        var boardDto = MapToDto(boardEntity);
        return ApiResponse<BoardDto>.SuccessResponse(boardDto, "Board created successfully", 201);
    }

    public async Task<ApiResponse<BoardDto>> UpdateAsync(Guid id, UpdateBoardWrite write)
    {
        var existingBoard = await _context.Boards.FirstOrDefaultAsync(b => b.Id == id);
        if (existingBoard == null)
        {
            return ApiResponse<BoardDto>.ErrorResponse("Board not found", 404);
        }

        if (await _archive.OnBoardAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<BoardDto>(archived, TreeItem.Board);
        }

        existingBoard.Name = write.Name;
        existingBoard.Description = write.Description;
        existingBoard.Visibility = write.Visibility;
        _context.StampChange(existingBoard, _actor);

        var tracked = _context.Entry(existingBoard);
        if (tracked.Changed())
        {
            var old = BoardFields.Changed(tracked);
            var workspace = await _tree.WorkspaceAsync(existingBoard.WorkspaceId);
            await _activity.AddAsync(
                ActivityType.UpdateBoard,
                new ActivityPlace(WorkspaceId: existingBoard.WorkspaceId, BoardId: existingBoard.Id),
                actor => new UpdateBoardData(actor, workspace, BoardRef.Of(existingBoard), old));
        }

        await _context.SaveChangesAsync();
        var updatedBoardDto = MapToDto(existingBoard);
        return ApiResponse<BoardDto>.SuccessResponse(updatedBoardDto, "Board updated successfully", 200);
    }

    public async Task<ApiResponse<BoardDto>> MoveAsync(Guid id, MoveBoardWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var board = await _context.Boards.FirstOrDefaultAsync(b => b.Id == id);
        if (board == null)
        {
            return ApiResponse<BoardDto>.ErrorResponse("Board not found", 404);
        }

        if (await _archive.OnBoardAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<BoardDto>(archived, TreeItem.Board);
        }

        var workspaceId = write.WorkspaceId;
        if (await _tree.FindWorkspaceAsync(workspaceId) is not { } workspace)
        {
            return ReferenceErrors.NotFound<BoardDto>("workspaceId", "Workspace", workspaceId);
        }

        var leaving = board.WorkspaceId;
        board.WorkspaceId = workspaceId;
        _context.StampChange(board, _actor);

        if (_context.Entry(board).Changed())
        {
            var from = await _tree.WorkspaceAsync(leaving);
            await _activity.AddAsync(
                ActivityType.MoveBoard,
                new ActivityPlace(
                    WorkspaceId: workspaceId, BoardId: board.Id, FromWorkspaceId: from.Id),
                actor => new MoveBoardData(actor, workspace, BoardRef.Of(board), new BoardOrigin(from)));
        }

        await _context.SaveChangesAsync();
        var movedBoardDto = MapToDto(board);
        return ApiResponse<BoardDto>.SuccessResponse(movedBoardDto, "Board moved successfully", 200);
    }

    public async Task<ApiResponse<bool>> SetArchivedAsync(Guid id, ArchivedWrite archived)
    {
        var board = await _context.Boards.FindAsync(id);
        if (board == null)
        {
            return ApiResponse<bool>.ErrorResponse("Board not found", 404);
        }

        return await _context.SetArchivedAsync(board, _actor, archived, "Board", async archiving =>
        {
            var workspace = await _tree.WorkspaceAsync(board.WorkspaceId);
            await _activity.AddAsync(
                archiving ? ActivityType.ArchiveBoard : ActivityType.RestoreBoard,
                new ActivityPlace(WorkspaceId: board.WorkspaceId, BoardId: board.Id),
                actor => new ArchiveBoardData(actor, workspace, BoardRef.Of(board)));
        });
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var existingBoard = await _context.Boards.FindAsync(id);
        if (existingBoard == null)
        {
            return ApiResponse<bool>.ErrorResponse("Board not found", 404);
        }

        if (!existingBoard.IsArchived)
        {
            return ArchiveErrors.NotArchived<bool>(TreeItem.Board);
        }

        var workspace = await _tree.WorkspaceAsync(existingBoard.WorkspaceId);
        _context.Boards.Remove(existingBoard);
        await _activity.AddAsync(
            ActivityType.DeleteBoard,
            new ActivityPlace(WorkspaceId: existingBoard.WorkspaceId, BoardId: existingBoard.Id),
            actor => new DeleteBoardData(actor, workspace, BoardRef.Of(existingBoard)));
        await _context.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Board deleted successfully", 204);
    }

    private static BoardDto MapToDto(Board board)
    {
        return new BoardDto
        (
            board.Id,
            board.Name,
            board.Description,
            board.WorkspaceId,
            board.Visibility,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt,
            board.CreatedBy,
            board.UpdatedBy
        );
    }

    private static Board MapToEntity(CreateBoardWrite write, Guid actorId)
    {
        return new Board
        {
            Id = Guid.NewGuid(),
            Name = write.Name,
            Description = write.Description,
            WorkspaceId = write.WorkspaceId,
            Visibility = write.Visibility,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
