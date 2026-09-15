using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/[controller]")]
public class BoardsController: ApiControllerBase
{
    private readonly IBoardService _boardService;

    public BoardsController(IBoardService boardService)
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
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<BoardDto>>> CreateBoard([FromBody] CreateBoardDto createBoardDto)
    {
        var response = await _boardService.CreateAsync(createBoardDto);
        if(!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetBoard), new { id = response.Data!.Id }, response);

    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<BoardDto>>> UpdateBoard(Guid id, [FromBody] UpdateBoardDto updateBoardDto)
    {
        var response = await _boardService.UpdateAsync(id, updateBoardDto);
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBoard(Guid id)
    {
        var response = await _boardService.DeleteAsync(id);
        return ToActionResult(response);
    }

}
