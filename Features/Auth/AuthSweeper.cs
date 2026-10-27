using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed partial class AuthSweeper : BackgroundService
{
    [LoggerMessage(EventId = 1, EventName = "SweepFailed", Level = LogLevel.Warning,
        Message = "An auth sweep failed. The next sweep retries it.")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

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
                SweepFailed(_logger, exception);
            }
        }
    }

    public async Task SweepAsync(CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = _time.GetUtcNow().UtcDateTime;
        var expiredTokens = context.AuthTokens.Where(t => t.ExpiresAt <= now);
        var replacedBefore = now - PasswordHistoryEntry.KeptFor;
        var oldHistory = context.PasswordHistory.Where(h => h.ReplacedAt <= replacedBefore);
        var signedUpBefore = now - UnverifiedLife;
        var unverified = context.Users.Where(u => !u.Verified && (u.UpdatedAt ?? u.CreatedAt) <= signedUpBefore);
        if (!await expiredTokens.AnyAsync(token) && !await oldHistory.AnyAsync(token) && !await unverified.AnyAsync(token))
        {
            return;
        }

        using var span = WorkerSpans.Worker.StartRoot(nameof(AuthSweeper));
        await InBatchesAsync(() => expiredTokens
            .OrderBy(t => t.Id)
            .Take(BatchSize)
            .ExecuteDeleteAsync(token));

        await InBatchesAsync(() => oldHistory
            .OrderBy(h => h.Id)
            .Take(BatchSize)
            .ExecuteDeleteAsync(token));

        // Postgres rechecks only the outer filter on a row a sign-up or verification changed while this waited.
        await InBatchesAsync(() => unverified
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
