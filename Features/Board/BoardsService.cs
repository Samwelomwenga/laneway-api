using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface IBoardService
{
    Task<PagedResponse<BoardDto>> GetAllAsync(BoardSearchDto searchDto);
    Task<ApiResponse<BoardDto>> GetByIdAsync(Guid id);
    Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardDto createBoardDto);
    Task<ApiResponse<BoardDto>> UpdateAsync(Guid id, UpdateBoardDto updateBoardDto);
    Task<ApiResponse<bool>> DeleteAsync(Guid id);
}

public class BoardService : IBoardService
{
    private readonly ApplicationDbContext _context;

    public BoardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResponse<BoardDto>> GetAllAsync(BoardSearchDto searchDto)
    {
        var query = _context.Boards.AsQueryable();

        if (!string.IsNullOrEmpty(searchDto.SearchTerm))
        {
            query = query.Where(b => b.Name.Contains(searchDto.SearchTerm) ||
                                     b.Description.Contains(searchDto.SearchTerm));
        }
        if (searchDto.IsArchived.HasValue)
        {
            query = query.Where(b => b.IsArchived == searchDto.IsArchived.Value);
        }

        if (searchDto.WorkspaceId.HasValue)
        {
            query = query.Where(b => b.WorkspaceId == searchDto.WorkspaceId.Value);
        }

        if (searchDto.Visibility.HasValue)
        {
            query = query.Where(b => b.Visibility == searchDto.Visibility.Value);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);
        var boards = await query
            .OrderBy(b => b.CreatedAt)
            .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
            .Take(searchDto.PageSize)
            .Include(b => b.Lists)
            .ToListAsync();
        var boardDtos = boards.Select(b => MapToDto(b, b.Lists.Select(l => l.Id).ToList())).ToList();

        return PagedResponse<BoardDto>.SuccessResponse(
            boardDtos,
            totalCount,
            searchDto.PageSize,
            searchDto.PageNumber,
            "Boards retrieved successfully"
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
        var boardDto = MapToDto(existingBoard, existingBoard.Lists.Select(l => l.Id).ToList());
        return ApiResponse<BoardDto>.SuccessResponse(boardDto, "Board retrieved successfully", 200);
    }

    public async Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardDto createBoardDto)
    {
        var workspaceId = createBoardDto.WorkspaceId!.Value;
        if (!await _context.WorkSpaces.AnyAsync(ws => ws.Id == workspaceId))
        {
            return WorkspaceNotFound(workspaceId);
        }

        var boardEntity = MapToEntity(createBoardDto);
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
        existingBoard.UpdatedAt = DateTime.UtcNow;
        existingBoard.UpdatedBy = Guid.NewGuid();

        await _context.SaveChangesAsync();
        var updatedBoardDto = MapToDto(existingBoard, existingBoard.Lists.Select(l => l.Id).ToList());
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

    private static Board MapToEntity(CreateBoardDto createBoardDto)
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
            CreatedBy = Guid.NewGuid()
        };
    }
}
