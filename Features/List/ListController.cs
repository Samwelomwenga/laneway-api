namespace DefaultNamespace;

public class ListController: ControllerBase
{
    private readonly IListService _listService;
    
    public ListController(IListService listService)
    {
        _listService = listService;
    }
    
    [HttpGet]
    public async Task<IActionResult<ApiResponse<PagedResponse<ListDto>>>> GetLists([FromQuery] ListSearchDto searchDto)
    {
        var response = await _listService.GetListsAsync(searchDto);
        return Ok(response);
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult<ApiResponse<ListDto>>> GetList(Guid id)
    {
        var response = await _listService.GetListByIdAsync(id);
        if (!response.success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    [HttpPost]
    public async Task<IActionResult<ApiResponse<ListDto>>> CreateList([FromBody] CreateListDto createListDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<ListDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _listService.CreateListAsync(createListDto);
        if (!response.success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetList), new { id = response.Data.Id }, response);
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult<ApiResponse<ListDto>>> UpdateList(Guid id, [FromBody] UpdateListDto updateListDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<ListDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _listService.UpdateListAsync(id, updateListDto);
        if (!response.success)
        {
            return BadRequest(response);
        }
        return Ok(response);
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult<ApiResponse<bool>>> DeleteList(Guid id)
    {
        var response = await _listService.DeleteListAsync(id);
        if (!response.success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    
}
