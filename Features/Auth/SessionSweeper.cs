using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed partial class SessionSweeper : BackgroundService
{
    [LoggerMessage(EventId = 1, EventName = "SweepFailed", Level = LogLevel.Warning,
        Message = "A session sweep failed. The next sweep retries it.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    public const int BatchSize = 1000;

    public static readonly TimeSpan KeptAfterFinish = TimeSpan.FromDays(30);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionSweeper> _logger;

    public SessionSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<SessionSweeper> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ticks = new PeriodicTimer(Interval);
        while (await ticks.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SweepFailed(_logger, exception);
            }
        }
    }

    public async Task<int> SweepAsync(CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cutoff = _time.GetUtcNow().UtcDateTime - KeptAfterFinish;
        var total = 0;
        while (true)
        {
            var deleted = await context.Sessions
                .Where(Session.FinishedBefore(cutoff))
                .OrderBy(session => session.Id)
                .Take(BatchSize)
                .ExecuteDeleteAsync(token);
            total += deleted;
            if (deleted < BatchSize)
            {
                return total;
            }
        }
    }
}
