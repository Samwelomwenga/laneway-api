namespace DefaultNamespace;

public class BoardController: ControllerBase
{
    private readonly IBoardService _boardService;

    public BoardController(IBoardService boardService)
    {
        _boardService = boardService;
    }

    [HttpGet]
    public async Task<IActionResult<ApiResponse<PagedResponse<BoardDto>>>> GetBoards([FromQuery] BoardSearchDto searchDto)
        var boards = await _boardService.GetAllAsync(searchDto);
        return Ok(boards);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBoard(Guid id)
    {
        var response = await _boardService.GetByIdAsync(id);
        if (!response.success)
        {
            return NotFound(response);
        }
        
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBoard([FromBody] Board board)
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
        var response = await _boardService.CreateAsync(board);
        if(!response.success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetBoard), new { id = response.Data.Id }, response);
        
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBoard(Guid id, [FromBody] Board board)
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
        var response = await _boardService.UpdateAsync(id, board);
        if (!response.success)
        {
            return NotFound(response);
        }
        return Ok(response);

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBoard(Guid id)
    {
        var response = await _boardService.DeleteAsync(id);
        if (!response.success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
}
