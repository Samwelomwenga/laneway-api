using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/[controller]")]
public class ListsController: ApiControllerBase
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
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> GetList(Guid id)
    {
        var response = await _listService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ListDto>>> CreateList([FromBody] CreateListDto createListDto)
    {
        var response = await _listService.CreateAsync(createListDto);
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetList), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> UpdateList(Guid id, [FromBody] UpdateListDto updateListDto)
    {
        var response = await _listService.UpdateAsync(id, updateListDto);
        return ToActionResult(response);
    }

    [HttpPut("{id}/archived")]
    public async Task<ActionResult<ApiResponse<bool>>> SetListArchived(Guid id, [FromBody] ArchivedDto archivedDto)
    {
        var response = await _listService.SetArchivedAsync(id, archivedDto);
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteList(Guid id)
    {
        var response = await _listService.DeleteAsync(id);
        return ToActionResult(response);
    }


}
