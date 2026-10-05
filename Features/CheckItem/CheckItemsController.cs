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

    [HttpGet]
    public async Task<ActionResult<PagedResponse<CheckItemDto>>> GetCheckItems(
        [FromQuery] CheckItemSearchDto searchDto)
    {
        var response = await _checkItemService.GetAllAsync(searchDto);
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CheckItemDto>>> GetCheckItem(Guid id)
    {
        var response = await _checkItemService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CheckItemDto>>> CreateCheckItem(
        [FromBody] CreateCheckItemDto createCheckItemDto)
    {
        var response = await _checkItemService.CreateAsync(CreateCheckItemWrite.Of(createCheckItemDto));
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
