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
    private readonly Readable _readable;

    public CopyJobService(ApplicationDbContext context, Actor actor, Readable readable)
    {
        _context = context;
        _actor = actor;
        _readable = readable;
    }

    public async Task<ApiResponse<CopyJobDto>> GetByIdAsync(Guid id)
    {
        var job = await _context.CopyJobs.AsNoTracking()
            .FirstOrDefaultAsync(found => found.Id == id && found.CreatedBy == _actor.Id);

        return job is null
            ? _readable.NotFound<CopyJobDto>(AccessTarget.CopyJob, id, _actor.Id)
            : ApiResponse<CopyJobDto>.SuccessResponse(
                CopyJobView.Of(job), "Copy job retrieved successfully", 200);
    }
}
