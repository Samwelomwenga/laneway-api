using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/[controller]")]
public class ChecklistsController : ApiControllerBase
{
    private readonly IChecklistService _checklistService;

    public ChecklistsController(IChecklistService checklistService)
    {
        _checklistService = checklistService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ChecklistDto>>> GetChecklists(
        [FromQuery] ChecklistSearchDto searchDto)
    {
        var response = await _checklistService.GetAllAsync(searchDto);
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ChecklistDto>>> GetChecklist(Guid id)
    {
        var response = await _checklistService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ChecklistDto>>> CreateChecklist(
        [FromBody] CreateChecklistDto createChecklistDto)
    {
        var response = await _checklistService.CreateAsync(CreateChecklistWrite.Of(createChecklistDto));
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetChecklist), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ChecklistDto>>> UpdateChecklist(
        Guid id, [FromBody] UpdateChecklistDto updateChecklistDto)
    {
        var response = await _checklistService.UpdateAsync(id, UpdateChecklistWrite.Of(updateChecklistDto));
        return ToActionResult(response);
    }

    [HttpPut("{id}/position")]
    public async Task<ActionResult<ApiResponse<ChecklistDto>>> ReorderChecklist(
        Guid id, [FromBody] ReorderChecklistDto reorderChecklistDto)
    {
        var response = await _checklistService.ReorderAsync(id, reorderChecklistDto);
        return ToActionResult(response);
    }

    [HttpPut("{id}/archived")]
    public async Task<ActionResult<ApiResponse<bool>>> SetChecklistArchived(
        Guid id, [FromBody] ArchivedDto archivedDto)
    {
        var response = await _checklistService.SetArchivedAsync(id, ArchivedWrite.Of(archivedDto));
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteChecklist(Guid id)
    {
        var response = await _checklistService.DeleteAsync(id);
        return ToActionResult(response);
    }
}
