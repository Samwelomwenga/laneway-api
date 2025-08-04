using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/[controller]")]
public class ListsController: ControllerBase
{
    private readonly IListService _listService;
    
    public ListsController(IListService listService)
    {
        _listService = listService;
    }
    
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ListDto>>> GetLists([FromQuery] ListSearchDto searchDto)
    {
        var response = await _listService.GetAllAsync(searchDto);
        return Ok(response);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> GetList(Guid id)
    {
        var response = await _listService.GetByIdAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ListDto>>> CreateList([FromBody] CreateListDto createListDto)
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
        var response = await _listService.CreateAsync(createListDto);
        if (!response.Success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetList), new { id = response.Data!.Id }, response);
    }
    
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> UpdateList(Guid id, [FromBody] UpdateListDto updateListDto)
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
        var response = await _listService.UpdateAsync(id, updateListDto);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteList(Guid id)
    {
        var response = await _listService.DeleteAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    
}
