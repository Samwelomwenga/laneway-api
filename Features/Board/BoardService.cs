namespace DefaultNamespace;

public interface IBoardService
{
    Task<List<Board>> GetAllAsync(BoardSearchDto searchDto);
    Task<Board?> GetByIdAsync(Guid id);
    Task<Board> CreateAsync(Board board);
    Task<Board?> UpdateAsync(Guid id, Board board);
    Task<bool> DeleteAsync(Guid id);
}

public class BoardService : IBoardService
{
    private readonly ApplicationDbContext _context;

    public BoardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Board>> GetAllAsync(BoardSearchDto searchDto)
    {
        try
        {
            var query = _context.Boards.AsQueryable();

            if (!string.IsNullOrEmpty(searchDto.SearchTerm))
            {
                query = query.Where(b => b.Name.Contains(searchDto.SearchTerm) || 
                                         b.Title.Contains(searchDto.SearchTerm) || 
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
            boards = query
                .OrderBy(b => b.CreatedAt)
                .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                .Take(searchDto.PageSize)
                .ToListAsync();
            var boardDto = boards.Select(MapToDto).ToList();
        
            var response = new PagedResult<BoardDto>
            {
                Data = boardDto,
                TotalCount = totalCount,
                PageSize = searchDto.PageSize,
                CurrentPage = searchDto.PageNumber,
                TotalPages = totalPages
            };
            
            return <ApiResponse<PagedResult<BoardDto>>>.SuccessResponse(response, "Boards retrieved successfully", 200);

        }
        catch (Exception e)
        {
            return <ApiResponse<List<BoardDto>>>.ErrorResponse("An error occurred while retrieving boards", 500,
                new List<string> { e.Message });
        }
       
    }

    public async Task<Board?> GetByIdAsync(Guid id)
    {
        try
        {
            var existingBoard = await _context.Boards.FindAsync(id);
            if (existingBoard == null)
            {
                return <ApiResponse<BoardDto>>.ErrorResponse("Board not found", 404);
            }
            var boardDto = MapToDto(existingBoard);
            return <ApiResponse<BoardDto>>.SuccessResponse(boardDto, "Board retrieved successfully", 200);

        }
        catch (Exception e)
        {
            return <ApiResponse<BoardDto>>.ErrorResponse("An error occurred while retrieving the board", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<Board> CreateAsync(Board board)
    {
        try
        {
            var boardEntity = MapToEntity(board);
            _context.Boards.Add(boardEntity);
            await _context.SaveChangesAsync();
            var boardDto = MapToDto(boardEntity);
            return <ApiResponse<BoardDto>>.SuccessResponse(boardDto, "Board created successfully", 201);

        }
        catch (Exception e)
        {
           return <ApiResponse<BoardDto>>.ErrorResponse("An error occurred while creating the board", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<Board?> UpdateAsync(Guid id, Board board)
    {
        try
        {
            var existingBoard = await _context.Boards.FindAsync(id);
            if (existingBoard == null)
            {
                return <ApiResponse<BoardDto>>.ErrorResponse("Board not found", 404);
            }

            existingBoard.Name = board.Name;
            existingBoard.Description = board.Description;
            existingBoard.Title = board.Title;
            existingBoard.WorkspaceId = board.WorkspaceId;
            existingBoard.OwnerId = board.OwnerId;
            existingBoard.Visibility = board.Visibility;
            existingBoard.IsArchived = board.IsArchived;
            existingBoard.UpdatedAt = DateTime.UtcNow;
            existingBoard.UpdatedBy = Guid.NewGuid(); // Assuming the updater's ID is set here

            await _context.SaveChangesAsync();
            var updatedBoardDto = MapToDto(existingBoard);
            return <ApiResponse<BoardDto>>.SuccessResponse(updatedBoardDto, "Board updated successfully", 200);

        }
        catch (Exception e)
        {
            return <ApiResponse<BoardDto>>.ErrorResponse("An error occurred while updating the board", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            var existingBoard = await _context.Boards.FindAsync(id);
            if (existingBoard == null)
            {
                return <ApiResponse<bool>>.ErrorResponse("Board not found", 404);
            }

            _context.Boards.Remove(existingBoard);
            await _context.SaveChangesAsync();
            return <ApiResponse<bool>>.SuccessResponse(true, "Board deleted successfully", 204);

        }
        catch (Exception e)
        {
            return ApiResponse<bool>.ErrorResponse("An error occurred while deleting the board", 500,
                new List<string> { e.Message });
        }
    }
}
private static BoardDto MapToDto(Board board)
{
    return new BoardDto
    (
        board.Id,
        board.Name,
        board.Description,
        board.Title,
        board.WorkspaceId,
        board.OwnerId,
        board.Visibility,
        board.IsArchived,
        board.CreatedAt,
        board.UpdatedAt,
        board.CreatedBy,
        board.UpdatedBy
    );
}

private static Board MapToEntity(CreateBoardDto createBoardDto)
{
    return new Board
    {
        Id = Guid.NewGuid(),
        Name = createBoardDto.Name,
        Description = createBoardDto.Description,
        Title = createBoardDto.Title,
        WorkspaceId = createBoardDto.WorkspaceId,
        OwnerId = createBoardDto.OwnerId,
        Visibility = createBoardDto.Visibility,
        IsArchived = createBoardDto.IsArchived,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Guid.NewGuid() // Assuming the creator's ID is set here
    };
}


