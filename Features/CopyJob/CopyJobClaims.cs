using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class CopyJobClaim : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    private readonly ApplicationDbContext _locked;

    internal CopyJobClaim(AsyncServiceScope scope, ApplicationDbContext locked, CopyJob job, int attempt)
    {
        _scope = scope;
        _locked = locked;
        Job = job;
        Attempt = attempt;
    }

    public CopyJob Job { get; }
    public int Attempt { get; }

    public async ValueTask DisposeAsync()
    {
        await _locked.Database.CloseConnectionAsync();
        await _scope.DisposeAsync();
    }
}

public sealed class CopyJobClaims
{
    public const int MaxAttempts = 3;

    private const int Candidates = 20;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CopyJobClaims> _logger;

    public CopyJobClaims(IServiceScopeFactory scopes, ILogger<CopyJobClaims> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<CopyJobClaim?> TryClaimAsync(CancellationToken token)
    {
        foreach (var id in await UnfinishedAsync(token))
        {
            if (await TryStartAsync(id, token) is { } claim)
            {
                return claim;
            }
        }

        return null;
    }

    private async Task<List<Guid>> UnfinishedAsync(CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.CopyJobs
            .AsNoTracking()
            .Where(job => job.FinishedAt == null)
            .OrderBy(job => job.CreatedAt)
            .ThenBy(job => job.Id)
            .Take(Candidates)
            .Select(job => job.Id)
            .ToListAsync(token);
    }

    private async Task<CopyJobClaim?> TryStartAsync(Guid id, CancellationToken token)
    {
        var scope = _scopes.CreateAsyncScope();
        try
        {
            var locked = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await locked.Database.OpenConnectionAsync(token);

            if (await TookLockAsync(locked, id, token) && await StartAsync(locked, id, token) is { } job)
            {
                return new CopyJobClaim(scope, locked, job, job.Attempts);
            }
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }

        await scope.DisposeAsync();
        return null;
    }

    private async Task<CopyJob?> StartAsync(ApplicationDbContext context, Guid id, CancellationToken token)
    {
        if (await context.CopyJobs.FirstOrDefaultAsync(found => found.Id == id, token) is not { } job
            || job.FinishedAt is not null)
        {
            return null;
        }

        if (job.Attempts >= MaxAttempts)
        {
            _logger.LogError("Copy job {JobId} gave up after {Attempts} attempts.", job.Id, job.Attempts);
            CopyJobEnd.Fail(job, [CopyJobEnd.InternalError]);
            await context.SaveChangesAsync(token);
            return null;
        }

        job.Status = CopyJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.Attempts++;
        await context.SaveChangesAsync(token);

        return job;
    }

    private static Task<bool> TookLockAsync(ApplicationDbContext context, Guid id, CancellationToken token) =>
        context.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKeyFor(id)}) AS \"Value\"")
            .SingleAsync(token);

    private static long LockKeyFor(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);

        return BitConverter.ToInt64(bytes) ^ BitConverter.ToInt64(bytes[8..]);
    }
}
