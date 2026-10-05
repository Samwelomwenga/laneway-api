using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/[controller]")]
public class LabelsController : ApiControllerBase
{
    private readonly ILabelService _labelService;
    public LabelsController(ILabelService labelService)
    {
        _labelService = labelService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<LabelDto>>> GetLabels([FromQuery] LabelSearchDto searchDto)
    {
        var response = await _labelService.GetAllAsync(searchDto);
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<LabelDto>>> GetLabel(Guid id)
    {
        var response = await _labelService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<LabelDto>>> CreateLabel([FromBody] CreateLabelDto createLabelDto)
    {
        var response = await _labelService.CreateAsync(CreateLabelWrite.Of(createLabelDto));
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetLabel), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<LabelDto>>> UpdateLabel(Guid id, [FromBody] UpdateLabelDto updateLabelDto)
    {
        var response = await _labelService.UpdateAsync(id, UpdateLabelWrite.Of(updateLabelDto));
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteLabel(Guid id)
    {
        var response = await _labelService.DeleteAsync(id);
        return ToActionResult(response);
    }
}
