using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/[controller]")]
public class BoardController: ControllerBase
{
    private readonly IBoardService _boardService;

    public BoardController(IBoardService boardService)
    {
        _boardService = boardService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<BoardDto>>> GetBoards([FromQuery] BoardSearchDto searchDto)
    {
        var boards = await _boardService.GetAllAsync(searchDto);
        return Ok(boards);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<BoardDto>>> GetBoard(Guid id)
    {
        var response = await _boardService.GetByIdAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<BoardDto>>> CreateBoard([FromBody] CreateBoardDto createBoardDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<BoardDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _boardService.CreateAsync(createBoardDto);
        if(!response.Success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetBoard), new { id = response.Data!.Id }, response);
        
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<BoardDto>>> UpdateBoard(Guid id, [FromBody] UpdateBoardDto updateBoardDto)
    {
        if(!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<BoardDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _boardService.UpdateAsync(id, updateBoardDto);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBoard(Guid id)
    {
        var response = await _boardService.DeleteAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
}
