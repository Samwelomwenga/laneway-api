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

    [HttpGet("/api/v1/workspaces/{workspaceId}/boards")]
    public async Task<ActionResult<PagedResponse<BoardDto>>> GetBoards(
        Guid workspaceId, [FromQuery] BoardSearchDto searchDto)
    {
        var boards = await _boardService.GetAllAsync(workspaceId, searchDto);
        return ToActionResult(boards);
    }

    [HttpGet("{id}")]
    [NoQuery]
    public async Task<ActionResult<ApiResponse<BoardViewDto>>> GetBoard(Guid id)
    {
        var response = await _boardService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost("/api/v1/workspaces/{workspaceId}/boards")]
    public async Task<ActionResult<ApiResponse<BoardDto>>> CreateBoard(
        Guid workspaceId, [FromBody] CreateBoardDto createBoardDto)
    {
        var response = await _boardService.CreateAsync(CreateBoardWrite.Of(workspaceId, createBoardDto));
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

    [HttpGet("{id}/settings")]
    [NoQuery]
    public async Task<ActionResult<ApiResponse<BoardSettingsDto>>> GetBoardSettings(Guid id)
    {
        var response = await _boardService.GetSettingsAsync(id);
        return ToActionResult(response);
    }

    [HttpPut("{id}/settings")]
    public async Task<ActionResult<ApiResponse<BoardSettingsDto>>> UpdateBoardSettings(
        Guid id, [FromBody] UpdateBoardSettingsDto updateBoardSettingsDto)
    {
        var response = await _boardService.UpdateSettingsAsync(id, BoardSettingsWrite.Of(updateBoardSettingsDto));
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
