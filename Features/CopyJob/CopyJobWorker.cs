using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class CopyJobWorker : BackgroundService
{
    private const int MaxJobs = 2;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly CopyJobClaims _claims;
    private readonly CopyJobSignal _signal;
    private readonly ILogger<CopyJobWorker> _logger;

    private readonly List<Task> _running = [];

    private DateTime _sweptAt = DateTime.MinValue;

    public CopyJobWorker(
        IServiceScopeFactory scopes,
        CopyJobClaims claims,
        CopyJobSignal signal,
        ILogger<CopyJobWorker> logger)
    {
        _scopes = scopes;
        _claims = claims;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(PollInterval, stoppingToken);
                await SweepAsync(stoppingToken);
                await ClaimAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A copy job poll failed. The next poll retries it.");
            }
        }

        await Task.WhenAll(_running);
    }

    private async Task ClaimAsync(CancellationToken token)
    {
        _running.RemoveAll(running => running.IsCompleted);
        while (_running.Count < MaxJobs && await _claims.TryClaimAsync(token) is { } claim)
        {
            _running.Add(RunAsync(claim, token));
        }
    }

    private async Task RunAsync(CopyJobClaim claim, CancellationToken token)
    {
        try
        {
            await using (claim)
            {
                if (await RunOnceAsync(claim, token) is { Outcome: CopyRun.Refused } refused)
                {
                    await FailAsync(claim.Job.Id, refused.Errors!, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception, "Copy job {JobId} threw on attempt {Attempt}.", claim.Job.Id, claim.Attempt);
            if (claim.Attempt >= CopyJobClaims.MaxAttempts)
            {
                await FailAsync(claim.Job.Id, [CopyJobEnd.InternalError], CancellationToken.None);
            }
        }
    }

    private async Task<CopyRunResult> RunOnceAsync(CopyJobClaim claim, CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Actor>().Resolve(claim.Job.CreatedBy);
        var runner = scope.ServiceProvider.GetServices<ICopyJobRunner>()
            .First(found => found.Kind == claim.Job.Kind);

        return await runner.RunAsync(claim.Job, claim.Attempt, token);
    }

    private async Task FailAsync(Guid id, List<ApiError> errors, CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await context.CopyJobs.FirstOrDefaultAsync(found => found.Id == id, token) is { } job)
        {
            CopyJobEnd.Fail(job, errors);
            await context.SaveChangesAsync(token);
        }
    }

    private async Task SweepAsync(CancellationToken token)
    {
        if (DateTime.UtcNow - _sweptAt < SweepInterval)
        {
            return;
        }

        _sweptAt = DateTime.UtcNow;
        await using var scope = _scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CopyJobSweep>().RunAsync(token);
    }
}
