using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/check-items")]
public class CheckItemsController : ApiControllerBase
{
    private readonly ICheckItemService _checkItemService;

    public CheckItemsController(ICheckItemService checkItemService)
    {
        _checkItemService = checkItemService;
    }

    [HttpGet("/api/v1/checklists/{checklistId}/check-items")]
    public async Task<ActionResult<PagedResponse<CheckItemDto>>> GetCheckItems(
        Guid checklistId, [FromQuery] CheckItemSearchDto searchDto)
    {
        var response = await _checkItemService.GetAllAsync(checklistId, searchDto);
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CheckItemDto>>> GetCheckItem(Guid id)
    {
        var response = await _checkItemService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost("/api/v1/checklists/{checklistId}/check-items")]
    public async Task<ActionResult<ApiResponse<CheckItemDto>>> CreateCheckItem(
        Guid checklistId, [FromBody] CreateCheckItemDto createCheckItemDto)
    {
        var response = await _checkItemService.CreateAsync(
            CreateCheckItemWrite.Of(checklistId, createCheckItemDto));
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetCheckItem), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<CheckItemDto>>> UpdateCheckItem(
        Guid id, [FromBody] UpdateCheckItemDto updateCheckItemDto)
    {
        var response = await _checkItemService.UpdateAsync(id, UpdateCheckItemWrite.Of(updateCheckItemDto));
        return ToActionResult(response);
    }

    [HttpPut("{id}/position")]
    public async Task<ActionResult<ApiResponse<CheckItemDto>>> MoveCheckItem(
        Guid id, [FromBody] MoveCheckItemDto moveCheckItemDto)
    {
        var response = await _checkItemService.MoveAsync(id, MoveCheckItemWrite.Of(moveCheckItemDto));
        return ToActionResult(response);
    }

    [HttpPut("{id}/checked")]
    public async Task<ActionResult<ApiResponse<bool>>> SetCheckItemChecked(
        Guid id, [FromBody] CheckedDto checkedDto)
    {
        var response = await _checkItemService.SetCheckedAsync(id, CheckedWrite.Of(checkedDto));
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteCheckItem(Guid id)
    {
        var response = await _checkItemService.DeleteAsync(id);
        return ToActionResult(response);
    }
}
