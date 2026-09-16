using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IBoardService
{
    Task<ApiResponse<List<BoardDto>>> GetAllAsync(BoardSearchDto searchDto);
    Task<ApiResponse<BoardDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardDto createBoardDto);
    Task<ApiResponse<BoardDto>> UpdateAsync(Guid id, UpdateBoardDto updateBoardDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class BoardService : IBoardService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;

    public BoardService(ApplicationDbContext context, Actor actor)
    {
        _context = context;
        _actor = actor;
    }

    public async Task<ApiResponse<List<BoardDto>>> GetAllAsync(BoardSearchDto searchDto)
    {
        if (searchDto.WorkspaceId is { } filterWorkspaceId
            && !await _context.WorkSpaces.AnyAsync(ws => ws.Id == filterWorkspaceId))
        {
            return ReferenceErrors.NotFound<List<BoardDto>>("workspaceId", "Workspace", filterWorkspaceId);
        }

        var query = _context.Boards.AsQueryable();

        if (searchDto.SearchTerm is { } term)
        {
            query = query.Where(b => b.Name.ToLower().Contains(term.ToLower()) ||
                                     b.Description.ToLower().Contains(term.ToLower()));
        }

        if (searchDto.WorkspaceId is { } workspaceId)
        {
            query = query.Where(b => b.WorkspaceId == workspaceId);
        }

        if (searchDto.IsArchived is { } isArchived)
        {
            query = query.Where(b => b.IsArchived == isArchived);
        }

        if (searchDto.Visibility is { } visibility)
        {
            query = query.Where(b => b.Visibility == visibility);
        }

        var (boards, totalCount) = await PagedQuery.ReadAsync(
            query.OrderBy(b => b.CreatedAt).Include(b => b.Lists),
            pageNumber: searchDto.PageNumber,
            pageSize: searchDto.PageSize);

        var boardDtos = boards.Select(b => MapToDto(b, b.Lists.InSortOrder().Select(l => l.Id).ToList())).ToList();

        return PagedResponse<BoardDto>.SuccessResponse(
            boardDtos,
            totalCount,
            pageSize: searchDto.PageSize,
            currentPage: searchDto.PageNumber,
            message: "Boards retrieved successfully"
        );
    }

    public async Task<ApiResponse<BoardDto>> GetByIdAsync(Guid id)
    {
        var existingBoard = await _context.Boards
            .Include(b => b.Lists)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (existingBoard == null)
        {
            return ApiResponse<BoardDto>.ErrorResponse("Board not found", 404);
        }
        var boardDto = MapToDto(existingBoard, existingBoard.Lists.InSortOrder().Select(l => l.Id).ToList());
        return ApiResponse<BoardDto>.SuccessResponse(boardDto, "Board retrieved successfully", 200);
    }

    public async Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardDto createBoardDto)
    {
        var workspaceId = createBoardDto.WorkspaceId!.Value;
        if (!await _context.WorkSpaces.AnyAsync(ws => ws.Id == workspaceId))
        {
            return WorkspaceNotFound(workspaceId);
        }

        var boardEntity = MapToEntity(createBoardDto, _actor.Id);
        _context.Boards.Add(boardEntity);
        await _context.SaveChangesAsync();
        var boardDto = MapToDto(boardEntity);
        return ApiResponse<BoardDto>.SuccessResponse(boardDto, "Board created successfully", 201);
    }

    public async Task<ApiResponse<BoardDto>> UpdateAsync(Guid id, UpdateBoardDto updateBoardDto)
    {
        var existingBoard = await _context.Boards
            .Include(b => b.Lists)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (existingBoard == null)
        {
            return ApiResponse<BoardDto>.ErrorResponse("Board not found", 404);
        }

        var workspaceId = updateBoardDto.WorkspaceId!.Value;
        if (!await _context.WorkSpaces.AnyAsync(ws => ws.Id == workspaceId))
        {
            return WorkspaceNotFound(workspaceId);
        }

        existingBoard.Name = updateBoardDto.Name!;
        existingBoard.Description = updateBoardDto.Description ?? string.Empty;
        existingBoard.WorkspaceId = workspaceId;
        existingBoard.Visibility = updateBoardDto.Visibility!.Value;
        existingBoard.IsArchived = updateBoardDto.IsArchived!.Value;
        _context.StampChange(existingBoard, _actor);

        await _context.SaveChangesAsync();
        var updatedBoardDto = MapToDto(existingBoard, existingBoard.Lists.InSortOrder().Select(l => l.Id).ToList());
        return ApiResponse<BoardDto>.SuccessResponse(updatedBoardDto, "Board updated successfully", 200);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var existingBoard = await _context.Boards.FindAsync(id);
        if (existingBoard == null)
        {
            return ApiResponse<bool>.ErrorResponse("Board not found", 404);
        }

        _context.Boards.Remove(existingBoard);
        await _context.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Board deleted successfully", 204);
    }

    private static BoardDto MapToDto(Board board, List<Guid>? listIds = null)
    {
        return new BoardDto
        (
            board.Id,
            board.Name,
            board.Description,
            board.WorkspaceId,
            board.Visibility,
            board.IsArchived,
            listIds ?? new List<Guid>(),
            board.CreatedAt,
            board.UpdatedAt,
            board.CreatedBy,
            board.UpdatedBy
        );
    }

    private static ApiResponse<BoardDto> WorkspaceNotFound(Guid workspaceId) =>
        ApiResponse<BoardDto>.ErrorResponse("A referenced resource does not exist", 400,
            [new ApiError("workspaceId", ErrorCodes.NotFound, $"Workspace {workspaceId} does not exist.")]);

    private static Board MapToEntity(CreateBoardDto createBoardDto, Guid actorId)
    {
        return new Board
        {
            Id = Guid.NewGuid(),
            Name = createBoardDto.Name!,
            Description = createBoardDto.Description ?? string.Empty,
            WorkspaceId = createBoardDto.WorkspaceId!.Value,
            Visibility = createBoardDto.Visibility!.Value,
            IsArchived = createBoardDto.IsArchived!.Value,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        };
    }
}
