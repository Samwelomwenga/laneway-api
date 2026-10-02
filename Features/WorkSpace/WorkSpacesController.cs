using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/v1/[controller]")]
public class WorkSpacesController : ApiControllerBase
{
    private readonly IWorkSpaceService _workSpaceService;

    public WorkSpacesController(IWorkSpaceService workSpaceService)
    {
        _workSpaceService = workSpaceService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<WorkSpaceDto>>> GetWorkSpaces([FromQuery] WorkSpaceSearchDto searchDto)
    {
        var response = await _workSpaceService.GetAllAsync(searchDto);
        return ToActionResult(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<WorkSpaceDto>>> GetWorkSpace(Guid id)
    {
        var response = await _workSpaceService.GetByIdAsync(id);
        return ToActionResult(response);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WorkSpaceDto>>> CreateWorkSpace([FromBody] CreateWorkSpaceDto createWorkSpaceDto)
    {
        var response = await _workSpaceService.CreateAsync(CreateWorkSpaceWrite.Of(createWorkSpaceDto));
        if (!response.Success)
        {
            return ToActionResult(response);
        }
        return CreatedAtAction(nameof(GetWorkSpace), new { id = response.Data!.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<WorkSpaceDto>>> UpdateWorkSpace(Guid id, [FromBody] UpdateWorkSpaceDto updateWorkSpaceDto)
    {
        var response = await _workSpaceService.UpdateAsync(id, UpdateWorkSpaceWrite.Of(updateWorkSpaceDto));
        return ToActionResult(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteWorkSpace(Guid id)
    {
        var response = await _workSpaceService.DeleteAsync(id);
        return ToActionResult(response);
    }
}
