using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class InviteSweeper : BackgroundService
{
    public const int BatchSize = 1000;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<InviteSweeper> _logger;

    public InviteSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<InviteSweeper> logger)
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
                _logger.LogWarning(exception, "An invite sweep failed. The next sweep retries it.");
            }
        }
    }

    public async Task<int> SweepAsync(CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = _time.GetUtcNow().UtcDateTime;
        var total = 0;
        while (true)
        {
            var deleted = await context.Invites
                .Where(invite => invite.ExpiresAt <= now)
                .OrderBy(invite => invite.Id)
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
