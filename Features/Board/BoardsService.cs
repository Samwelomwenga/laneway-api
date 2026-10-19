using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface IBoardService
{
    Task<ApiResponse<List<BoardDto>>> GetAllAsync(Guid workspaceId, BoardSearchDto searchDto);
    Task<ApiResponse<BoardViewDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardWrite write);
    Task<ApiResponse<BoardDto>> UpdateAsync(Guid id, UpdateBoardWrite write);
    Task<ApiResponse<BoardSettingsDto>> GetSettingsAsync(Guid id);
    Task<ApiResponse<BoardSettingsDto>> UpdateSettingsAsync(Guid id, BoardSettingsWrite write);
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
    private readonly Readable _readable;

    public BoardService(
        ApplicationDbContext context,
        Actor actor,
        ArchiveGuard archive,
        ActivityWriter activity,
        ActivityTree tree,
        CardReads cards,
        Readable readable)
    {
        _context = context;
        _actor = actor;
        _archive = archive;
        _activity = activity;
        _tree = tree;
        _cards = cards;
        _readable = readable;
    }

    public async Task<ApiResponse<List<BoardDto>>> GetAllAsync(Guid workspaceId, BoardSearchDto searchDto)
    {
        var userId = _actor.Id;
        var access = await _readable.WorkspaceAsync(workspaceId, userId);
        if (_readable.ReadDenies(access, AccessTarget.Workspace, workspaceId, userId, out ApiResponse<List<BoardDto>>? denied))
        {
            return denied;
        }

        var query = _readable.Boards(userId).Where(b => b.WorkspaceId == workspaceId);
        if (!access.Standing.IsMember)
        {
            var publicWorkspace = access.Target.Visibility == WorkspaceVisibility.Public;
            query = query.Where(b =>
                _context.BoardMembers.Any(member => member.BoardId == b.Id && member.UserId == userId)
                || (publicWorkspace && b.Visibility == BoardVisibility.Public));
        }

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
        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.ReadDenies(access, AccessTarget.Board, id, _actor.Id, out ApiResponse<BoardViewDto>? denied))
        {
            return denied;
        }

        var existingBoard = access.Target;

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
        var access = await _readable.WorkspaceAsync(workspaceId, _actor.Id);
        if (_readable.Denies(access, WorkspaceAction.CreateBoard, workspaceId, _actor.Id, out ApiResponse<BoardDto>? denied))
        {
            return denied;
        }

        var workspace = WorkspaceRef.Of(access.Target);
        var boardEntity = MapToEntity(write, _actor.Id);
        _context.Boards.Add(boardEntity);
        _context.BoardMembers.Add(BoardMember.AdminOf(boardEntity));
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
        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Edit, AccessTarget.Board, id, _actor.Id, out ApiResponse<BoardDto>? denied))
        {
            return denied;
        }

        if (await _archive.OnBoardAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<BoardDto>(archived, TreeItem.Board);
        }

        var existingBoard = access.Target;
        existingBoard.Name = write.Name;
        existingBoard.Description = write.Description;
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

    public async Task<ApiResponse<BoardSettingsDto>> GetSettingsAsync(Guid id)
    {
        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.ReadDenies(access, AccessTarget.Board, id, _actor.Id, out ApiResponse<BoardSettingsDto>? denied))
        {
            return denied;
        }

        return ApiResponse<BoardSettingsDto>.SuccessResponse(SettingsOf(access.Target), "Board settings retrieved successfully");
    }

    public async Task<ApiResponse<BoardSettingsDto>> UpdateSettingsAsync(Guid id, BoardSettingsWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Administer, AccessTarget.Board, id, _actor.Id, out ApiResponse<BoardSettingsDto>? denied))
        {
            return denied;
        }

        if (await _archive.OnBoardAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<BoardSettingsDto>(archived, TreeItem.Board);
        }

        var board = access.Target;
        board.Commenting = write.Commenting;
        board.AddingMembers = write.AddingMembers;
        board.SelfJoin = write.SelfJoin;
        board.Visibility = write.Visibility;
        _context.StampChange(board, _actor);

        var tracked = _context.Entry(board);
        if (tracked.Changed())
        {
            var old = BoardSettingsFields.Changed(tracked);
            var workspace = await _tree.WorkspaceAsync(board.WorkspaceId);
            await _activity.AddAsync(
                ActivityType.UpdateBoardSettings,
                new ActivityPlace(WorkspaceId: board.WorkspaceId, BoardId: board.Id),
                actor => new UpdateBoardSettingsData(actor, workspace, BoardRef.Of(board), old));
        }

        await _context.SaveChangesAsync();
        return ApiResponse<BoardSettingsDto>.SuccessResponse(SettingsOf(board), "Board settings updated successfully");
    }

    public async Task<ApiResponse<BoardDto>> MoveAsync(Guid id, MoveBoardWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Administer, AccessTarget.Board, id, _actor.Id, out ApiResponse<BoardDto>? denied))
        {
            return denied;
        }

        var workspaceId = write.WorkspaceId;
        var target = await _readable.WorkspaceAsync(workspaceId, _actor.Id);
        if (_readable.TargetDenies(target, WorkspaceAction.CreateBoard, "workspaceId", workspaceId, _actor.Id, out denied))
        {
            return denied;
        }

        if (await _archive.OnBoardAsync(id) is { } archived)
        {
            return ArchiveErrors.ReadOnly<BoardDto>(archived, TreeItem.Board);
        }

        var board = access.Target;
        var workspace = await _tree.WorkspaceAsync(workspaceId);

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
        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Administer, AccessTarget.Board, id, _actor.Id, out ApiResponse<bool>? denied))
        {
            return denied;
        }

        var board = access.Target;
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
        var access = await _readable.BoardAsync(id, _actor.Id);
        if (_readable.Denies(access, BoardAction.Administer, AccessTarget.Board, id, _actor.Id, out ApiResponse<bool>? denied))
        {
            return denied;
        }

        var existingBoard = access.Target;
        if (!existingBoard.IsArchived)
        {
            return ArchiveErrors.NotArchived<bool>(TreeItem.Board);
        }

        var workspace = await _tree.WorkspaceAsync(existingBoard.WorkspaceId);
        await _context.KeepBoardReadersAsync(existingBoard);
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

    private static BoardSettingsDto SettingsOf(Board board) =>
        new(board.Commenting, board.AddingMembers, board.SelfJoin, board.Visibility);

    private static Board MapToEntity(CreateBoardWrite write, Guid actorId)
    {
        return new Board
        {
            Id = Guid.NewGuid(),
            Name = write.Name,
            Description = write.Description,
            WorkspaceId = write.WorkspaceId,
            Visibility = write.Settings.Visibility,
            Commenting = write.Settings.Commenting,
            AddingMembers = write.Settings.AddingMembers,
            SelfJoin = write.Settings.SelfJoin,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
