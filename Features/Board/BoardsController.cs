using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/[controller]")]
public class BoardsController : ApiControllerBase
{
    private readonly IBoardService _boardService;
    private readonly IBoardCopyService _boardCopyService;

    public BoardsController(IBoardService boardService, IBoardCopyService boardCopyService)
    {
        _boardService = boardService;
        _boardCopyService = boardCopyService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<BoardDto>>> GetBoards([FromQuery] BoardSearchDto searchDto)
    {
        var boards = await _boardService.GetAllAsync(searchDto);
        return ToActionResult(boards);
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
        var response = await _boardService.CreateAsync(CreateBoardWrite.Of(createBoardDto));
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetBoard), new { id = response.Data!.Id }, response);

    }

    [HttpPost("{id}/copies")]
    public async Task<ActionResult<ApiResponse<CopyJobDto>>> CopyBoard(
        Guid id, [FromBody] CopyBoardDto copyBoardDto)
    {
        var response = await _boardCopyService.CopyAsync(id, CopyBoardWrite.Of(copyBoardDto));
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return AcceptedAtAction(
            nameof(CopyJobsController.GetCopyJob), "CopyJobs", new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<BoardDto>>> UpdateBoard(Guid id, [FromBody] UpdateBoardDto updateBoardDto)
    {
        var response = await _boardService.UpdateAsync(id, UpdateBoardWrite.Of(updateBoardDto));
        return ToActionResult(response);
    }

    [HttpPut("{id}/workspace")]
    public async Task<ActionResult<ApiResponse<BoardDto>>> MoveBoard(Guid id, [FromBody] MoveBoardDto moveBoardDto)
    {
        var response = await _boardService.MoveAsync(id, MoveBoardWrite.Of(moveBoardDto));
        return ToActionResult(response);
    }

    [HttpPut("{id}/archived")]
    public async Task<ActionResult<ApiResponse<bool>>> SetBoardArchived(Guid id, [FromBody] ArchivedDto archivedDto)
    {
        var response = await _boardService.SetArchivedAsync(id, ArchivedWrite.Of(archivedDto));
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBoard(Guid id)
    {
        var response = await _boardService.DeleteAsync(id);
        return ToActionResult(response);
    }

}
