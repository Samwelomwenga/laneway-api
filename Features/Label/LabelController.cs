using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/[controller]")]
public class LabelController: ControllerBase
{
    private readonly ILabelService _labelService;
    public LabelController(ILabelService labelService)
    {
        _labelService = labelService;
    }
    
    [HttpGet]
    public async Task<ActionResult<PagedResponse<LabelDto>>> GetLabels([FromQuery] LabelSearchDto searchDto)
    {
        var response = await _labelService.GetAllAsync(searchDto);
        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<LabelDto>>> GetLabel(Guid id)
    {
        var response = await _labelService.GetByIdAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<LabelDto>>> CreateLabel([FromBody] CreateLabelDto createLabelDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<LabelDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _labelService.CreateAsync(createLabelDto);
        if (!response.Success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetLabel), new { id = response.Data!.Id }, response);
    }
    
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<LabelDto>>> UpdateLabel(Guid id, [FromBody] UpdateLabelDto updateLabelDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<LabelDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _labelService.UpdateAsync(id, updateLabelDto);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteLabel(Guid id)
    {
        var response = await _labelService.DeleteAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
}
