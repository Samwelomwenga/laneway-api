using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class CopyJobSweep
{
    public static TimeSpan JobLife { get; } = TimeSpan.FromDays(7);

    private readonly ApplicationDbContext _context;

    public CopyJobSweep(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<int> RunAsync(CancellationToken token)
    {
        var cutoff = DateTime.UtcNow - JobLife;
        return _context.CopyJobs.Where(job => job.FinishedAt < cutoff).ExecuteDeleteAsync(token);
    }
}
