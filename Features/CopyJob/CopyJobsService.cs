using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public interface ICopyJobService
{
    Task<ApiResponse<CopyJobDto>> GetByIdAsync(Guid id);
}

public sealed class CopyJobService : ICopyJobService
{
    private readonly ApplicationDbContext _context;
    private readonly Actor _actor;

    public CopyJobService(ApplicationDbContext context, Actor actor)
    {
        _context = context;
        _actor = actor;
    }

    public async Task<ApiResponse<CopyJobDto>> GetByIdAsync(Guid id)
    {
        var job = await _context.CopyJobs.AsNoTracking()
            .FirstOrDefaultAsync(found => found.Id == id && found.CreatedBy == _actor.Id);

        return job is null
            ? ApiResponse<CopyJobDto>.ErrorResponse("Copy job not found", 404)
            : ApiResponse<CopyJobDto>.SuccessResponse(
                CopyJobView.Of(job), "Copy job retrieved successfully", 200);
    }
}
