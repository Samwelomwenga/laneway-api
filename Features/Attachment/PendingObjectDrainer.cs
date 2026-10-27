using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed partial class PendingObjectDrainer : BackgroundService
{
    [LoggerMessage(EventId = 1, EventName = "ObjectsAlreadyGone", Level = LogLevel.Information,
        Message = "{Missing} of {Batch} pending object deletes had no object left, and count as gone.")]
    private static partial void ObjectsAlreadyGone(ILogger logger, int missing, int batch);

    [LoggerMessage(EventId = 2, EventName = "DrainFailed", Level = LogLevel.Warning,
        Message = "A batch of pending object deletes failed. The next drain retries it.")]
    private static partial void DrainFailed(ILogger logger, Exception exception);

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
            var batch = await NextBatchAsync(context, token);
            if (batch.Count == 0)
            {
                return;
            }

            using var span = WorkerSpans.Worker.StartRoot(nameof(PendingObjectDrainer));
            while (batch.Count > 0)
            {
                var removed = await _storage.RemoveAsync(batch.ConvertAll(pending => pending.ObjectKey));
                if (removed < batch.Count)
                {
                    ObjectsAlreadyGone(_logger, batch.Count - removed, batch.Count);
                }

                context.PendingObjectDeletes.RemoveRange(batch);
                await context.SaveChangesAsync(token);

                if (batch.Count < BatchSize)
                {
                    return;
                }

                batch = await NextBatchAsync(context, token);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DrainFailed(_logger, exception);
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
