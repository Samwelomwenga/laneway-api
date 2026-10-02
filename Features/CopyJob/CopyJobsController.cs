using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/copy-jobs")]
public class CopyJobsController : ApiControllerBase
{
    private readonly ICopyJobService _copyJobService;

    public CopyJobsController(ICopyJobService copyJobService)
    {
        _copyJobService = copyJobService;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CopyJobDto>>> GetCopyJob(Guid id)
    {
        var response = await _copyJobService.GetByIdAsync(id);
        return ToActionResult(response);
    }
}
