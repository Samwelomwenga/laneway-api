using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

public interface ICopyJobService
{
    Task<ApiResponse<CopyJobDto>> GetByIdAsync(Guid id);
}

public sealed class CopyJobService : ICopyJobService
{
    private readonly ApplicationDbContext _context;

    public CopyJobService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<CopyJobDto>> GetByIdAsync(Guid id)
    {
        var job = await _context.CopyJobs.AsNoTracking().FirstOrDefaultAsync(found => found.Id == id);

        return job is null
            ? ApiResponse<CopyJobDto>.ErrorResponse("Copy job not found", 404)
            : ApiResponse<CopyJobDto>.SuccessResponse(
                CopyJobView.Of(job), "Copy job retrieved successfully", 200);
    }
}
