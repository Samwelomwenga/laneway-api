using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class AuthSweeper : BackgroundService
{
    public const int BatchSize = 1000;

    public static readonly TimeSpan UnverifiedLife = TimeSpan.FromDays(7);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<AuthSweeper> _logger;

    public AuthSweeper(IServiceScopeFactory scopes, TimeProvider time, ILogger<AuthSweeper> logger)
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
                _logger.LogWarning(exception, "An auth sweep failed. The next sweep retries it.");
            }
        }
    }

    public async Task SweepAsync(CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = _time.GetUtcNow().UtcDateTime;
        await InBatchesAsync(() => context.AuthTokens
            .Where(t => t.ExpiresAt <= now)
            .OrderBy(t => t.Id)
            .Take(BatchSize)
            .ExecuteDeleteAsync(token));

        // Postgres rechecks only the outer filter on a row a sign-up or verification changed while this waited.
        var signedUpBefore = now - UnverifiedLife;
        await InBatchesAsync(() => context.Users
            .Where(u => !u.Verified && (u.UpdatedAt ?? u.CreatedAt) <= signedUpBefore)
            .Where(u => context.Users
                .Where(batch => !batch.Verified && (batch.UpdatedAt ?? batch.CreatedAt) <= signedUpBefore)
                .OrderBy(batch => batch.Id)
                .Take(BatchSize)
                .Select(batch => batch.Id)
                .Contains(u.Id))
            .ExecuteDeleteAsync(token));
    }

    private static async Task InBatchesAsync(Func<Task<int>> deleteBatch)
    {
        int deleted;
        do
        {
            deleted = await deleteBatch();
        }
        while (deleted == BatchSize);
    }
}
