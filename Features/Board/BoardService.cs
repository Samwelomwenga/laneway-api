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
        try
        {
            var query = _context.Boards.AsQueryable();

            if (!string.IsNullOrEmpty(searchDto.SearchTerm))
            {
                query = query.Where(b => b.Name.Contains(searchDto.SearchTerm) || 
                                         (b.Title != null && b.Title.Contains(searchDto.SearchTerm)) || 
                                         (b.Description != null && b.Description.Contains(searchDto.SearchTerm)));
            }
            if (searchDto.IsArchived.HasValue)
            {
                query = query.Where(b => b.IsArchived == searchDto.IsArchived.Value);
            }

            if (searchDto.WorkspaceId.HasValue)
            {
                query = query.Where(b => b.WorkspaceId == searchDto.WorkspaceId.Value);
            }

            if (!string.IsNullOrEmpty(searchDto.Visibility))
            {
                query = query.Where(b => b.Visibility == searchDto.Visibility);
            }
        
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize);
            var boards = await query
                .OrderBy(b => b.CreatedAt)
                .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                .Take(searchDto.PageSize)
                .ToListAsync();
            var boardDtos = boards.Select(MapToDto).ToList();
        
            return new PagedResponse<BoardDto>
            {
                Data = boardDtos,
                TotalCount = totalCount,
                PageSize = searchDto.PageSize,
                CurrentPage = searchDto.PageNumber,
                TotalPages = totalPages
            };
        }
        catch (Exception e)
        {
            return PagedResponse<BoardDto>.ErrorResponse("An error occurred while retrieving boards", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<BoardDto>> GetByIdAsync(Guid id)
    {
        try
        {
            var existingBoard = await _context.Boards.FindAsync(id);
            if (existingBoard == null)
            {
                return ApiResponse<BoardDto>.ErrorResponse("Board not found", 404);
            }
            var boardDto = MapToDto(existingBoard);
            return ApiResponse<BoardDto>.SuccessResponse(boardDto, "Board retrieved successfully", 200);
        }
        catch (Exception e)
        {
            return ApiResponse<BoardDto>.ErrorResponse("An error occurred while retrieving the board", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<BoardDto>> CreateAsync(CreateBoardDto createBoardDto)
    {
        try
        {
            var boardEntity = MapToEntity(createBoardDto);
            _context.Boards.Add(boardEntity);
            await _context.SaveChangesAsync();
            var boardDto = MapToDto(boardEntity);
            return ApiResponse<BoardDto>.SuccessResponse(boardDto, "Board created successfully", 201);
        }
        catch (Exception e)
        {
           return ApiResponse<BoardDto>.ErrorResponse("An error occurred while creating the board", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<BoardDto>> UpdateAsync(Guid id, UpdateBoardDto updateBoardDto)
    {
        try
        {
            var existingBoard = await _context.Boards.FindAsync(id);
            if (existingBoard == null)
            {
                return ApiResponse<BoardDto>.ErrorResponse("Board not found", 404);
            }

            existingBoard.Name = updateBoardDto.Name;
            existingBoard.Description = updateBoardDto.Description;
            existingBoard.Title = updateBoardDto.Title;
            existingBoard.WorkspaceId = updateBoardDto.WorkspaceId;
            existingBoard.OwnerId = updateBoardDto.OwnerId;
            existingBoard.Visibility = updateBoardDto.Visibility;
            existingBoard.IsArchived = updateBoardDto.IsArchived;
            existingBoard.UpdatedAt = DateTime.UtcNow;
            existingBoard.UpdatedBy = Guid.NewGuid(); // This should be set to the current user's ID

            await _context.SaveChangesAsync();
            var updatedBoardDto = MapToDto(existingBoard);
            return ApiResponse<BoardDto>.SuccessResponse(updatedBoardDto, "Board updated successfully", 200);
        }
        catch (Exception e)
        {
            return ApiResponse<BoardDto>.ErrorResponse("An error occurred while updating the board", 500,
                new List<string> { e.Message });
        }
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        try
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
        catch (Exception e)
        {
            return ApiResponse<bool>.ErrorResponse("An error occurred while deleting the board", 500,
                new List<string> { e.Message });
        }
    }

    private static BoardDto MapToDto(Board board)
    {
        return new BoardDto
        (
            board.Id,
            board.Name,
            board.Description ?? string.Empty,
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
            CreatedBy = Guid.NewGuid() // This should be set to the current user's ID
        };
    }
}