using Microsoft.AspNetCore.Mvc;

namespace Laneway.Api;

[ApiController]
[Route("api/v1/[controller]")]
public class ActivityController : ApiControllerBase
{
    private readonly IActivityService _activityService;

    public ActivityController(IActivityService activityService)
    {
        _activityService = activityService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ActivityEntryDto>>> GetActivity(
        [FromQuery] ActivitySearchDto searchDto)
    {
        var response = await _activityService.GetAllAsync(searchDto);
        return ToActionResult(response);
    }
}
