namespace DefaultNamespace;

public class LabelController: ControllerBase
{
    private readonly ILabelService _labelService;
    public LabelController(ILabelService labelService)
    {
        _labelService = labelService;
    }
    
    [HttpGet]
    public async Task<IActionResult<ApiResponse<PagedResponse<LabelDto>>>> GetLabels([FromQuery] LabelSearchDto searchDto)
    {
        var response = await _labelService.GetLabelsAsync(searchDto);
        return Ok(response);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult<ApiResponse<LabelDto>>> GetLabel(Guid id)
    {
        var response = await _labelService.GetLabelByIdAsync(id);
        if (!response.success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    [HttpPost]
    public async Task<IActionResult<ApiResponse<LabelDto>>> CreateLabel([FromBody] CreateLabelDto createLabelDto)
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
        var response = await _labelService.CreateLabelAsync(createLabelDto);
        if (!response.success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetLabel), new { id = response.Data.Id }, response);
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult<ApiResponse<LabelDto>>> UpdateLabel(Guid id, [FromBody] UpdateLabelDto updateLabelDto)
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
        var response = await _labelService.UpdateLabelAsync(id, updateLabelDto);
        if (!response.success)
        {
            return BadRequest(response);
        }
        return Ok(response);
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult<ApiResponse<bool>>> DeleteLabel(Guid id)
    {
        var response = await _labelService.DeleteLabelAsync(id);
        if (!response.success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
}
