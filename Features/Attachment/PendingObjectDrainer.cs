using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class PendingObjectDrainer : BackgroundService
{
    private const int BatchSize = 100;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly AttachmentStorage _storage;
    private readonly ILogger<PendingObjectDrainer> _logger;

    public PendingObjectDrainer(
        IServiceScopeFactory scopes, AttachmentStorage storage, ILogger<PendingObjectDrainer> logger)
    {
        _scopes = scopes;
        _storage = storage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ticks = new PeriodicTimer(Interval);
        while (await ticks.WaitForNextTickAsync(stoppingToken))
        {
            await DrainAsync(stoppingToken);
        }
    }

    private async Task DrainAsync(CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            while (await NextBatchAsync(context, token) is { Count: > 0 } batch)
            {
                var removed = await _storage.RemoveAsync(batch.ConvertAll(pending => pending.ObjectKey));
                if (removed < batch.Count)
                {
                    _logger.LogInformation(
                        "{Missing} of {Batch} pending object deletes had no object left, and count as gone.",
                        batch.Count - removed, batch.Count);
                }

                context.PendingObjectDeletes.RemoveRange(batch);
                await context.SaveChangesAsync(token);

                if (batch.Count < BatchSize)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "A batch of pending object deletes failed. The next drain retries it.");
        }
    }

    private static Task<List<PendingObjectDelete>> NextBatchAsync(
        ApplicationDbContext context, CancellationToken token) =>
        context.PendingObjectDeletes
            .Where(pending => pending.NotBefore <= DateTime.UtcNow)
            .OrderBy(pending => pending.NotBefore)
            .Take(BatchSize)
            .ToListAsync(token);
}
