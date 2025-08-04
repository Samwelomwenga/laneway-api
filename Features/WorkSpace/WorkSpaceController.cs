using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace DefaultNamespace;

[ApiController]
[Route("api/[controller]")]
public class WorkSpaceController: ControllerBase
{
    private readonly IWorkSpaceService _workSpaceService;

    public WorkSpaceController(IWorkSpaceService workSpaceService)
    {
        _workSpaceService = workSpaceService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<WorkSpaceDto>>> GetWorkSpaces([FromQuery] WorkSpaceSearchDto searchDto)
    {
        var response = await _workSpaceService.GetAllAsync(searchDto);
        return Ok(response);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<WorkSpaceDto>>> GetWorkSpace(Guid id)
    {
        var response = await _workSpaceService.GetByIdAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    [HttpPost]
    public async Task<ActionResult<ApiResponse<WorkSpaceDto>>> CreateWorkSpace([FromBody] CreateWorkSpaceDto createWorkSpaceDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<WorkSpaceDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _workSpaceService.CreateAsync(createWorkSpaceDto);
        if (!response.Success)
        {
            return BadRequest(response);
        }
        return CreatedAtAction(nameof(GetWorkSpace), new { id = response.Data!.Id }, response);
    }
    
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<WorkSpaceDto>>> UpdateWorkSpace(Guid id, [FromBody] UpdateWorkSpaceDto updateWorkSpaceDto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            var errorResponse = ApiResponse<WorkSpaceDto>.ErrorResponse("Invalid data", 400, errors);
            return BadRequest(errorResponse);
        }
        var response = await _workSpaceService.UpdateAsync(id, updateWorkSpaceDto);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
    
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteWorkSpace(Guid id)
    {
        var response = await _workSpaceService.DeleteAsync(id);
        if (!response.Success)
        {
            return NotFound(response);
        }
        return Ok(response);
    }
}
