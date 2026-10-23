using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class DeliveryWorker : BackgroundService
{
    private const int Candidates = 20;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly DeliverySignal _signal;
    private readonly AppLinks _links;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    public DeliveryWorker(
        IServiceScopeFactory scopes, DeliverySignal signal, AppLinks links, TimeProvider time, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        _scopes = scopes;
        _signal = signal;
        _links = links;
        _time = time;
        _log = loggers.CreateLogger(MessageLog.Category);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(PollInterval, stoppingToken);
                while (await ClaimAsync(stoppingToken) is { } delivery)
                {
                    await SendAsync(delivery, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _log.LogWarning(exception, "A delivery poll failed. The next poll retries it.");
            }
        }
    }

    private async Task<Delivery?> ClaimAsync(CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = _time.GetUtcNow().UtcDateTime;
        var due = await database.Deliveries
            .AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Select(d => d.Id)
            .Take(Candidates)
            .ToListAsync(token);

        foreach (var id in due)
        {
            var claimed = await database.Deliveries
                .Where(d => d.Id == id && d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
                .ExecuteUpdateAsync(
                    d => d.SetProperty(e => e.NextAttemptAt, now + Lease).SetProperty(e => e.Attempts, e => e.Attempts + 1),
                    token);
            if (claimed == 1)
            {
                return await database.Deliveries.AsNoTracking().SingleAsync(d => d.Id == id, token);
            }
        }

        return null;
    }

    private async Task SendAsync(Delivery delivery, CancellationToken token)
    {
        using var logScope = _log.BeginDeliveryScope(delivery);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var address = delivery.Address
                ?? await database.Users.Where(u => u.Id == delivery.UserId).Select(u => u.Email).SingleOrDefaultAsync(token);
            if (address is null)
            {
                await EndAsync(database, delivery, DeliveryStatus.Dropped, providerMessageId: null, delivery.Address, token);
                return;
            }

            var body = EmailTemplates.Render(delivery.Type, delivery.Message, _links);
            var sent = await scope.ServiceProvider.GetRequiredService<IEmailProvider>().SendAsync(
                new EmailContent(address, body.Subject, EmailLayout.Html(body), EmailLayout.Text(body), delivery.Type.ToString()),
                delivery.IdempotencyKey,
                token);

            await EndAsync(database, delivery, DeliveryStatus.Sent, sent.ProviderMessageId, address, token);
            _log.DeliverySent(sent.ProviderMessageId);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "A delivery send threw. The claim's lease runs out and the next poll retries it.");
        }
    }

    private static Task<int> EndAsync(
        ApplicationDbContext database,
        Delivery claimed,
        DeliveryStatus status,
        string? providerMessageId,
        string? address,
        CancellationToken token) =>
        database.Deliveries
            .Where(d => d.Id == claimed.Id && d.Status == DeliveryStatus.Pending && d.Attempts == claimed.Attempts)
            .ExecuteUpdateAsync(
                d => d
                    .SetProperty(e => e.Status, status)
                    .SetProperty(e => e.ProviderMessageId, providerMessageId)
                    .SetProperty(e => e.Address, address),
                token);
}
