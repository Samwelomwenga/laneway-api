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

    public async Task<int> RunAsync(CancellationToken token)
    {
        var cutoff = DateTime.UtcNow - JobLife;
        var old = _context.CopyJobs.Where(job => job.FinishedAt < cutoff);
        if (!await old.AnyAsync(token))
        {
            return 0;
        }

        using var span = WorkerSpans.CopyJob.StartRoot(nameof(CopyJobSweep));
        return await old.ExecuteDeleteAsync(token);
    }
}
