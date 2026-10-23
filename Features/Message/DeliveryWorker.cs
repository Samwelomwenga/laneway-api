using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public sealed class DeliveryWorker : BackgroundService
{
    private const int Candidates = 20;

    private const int MaxSends = 4;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    private static readonly int MaxAttempts = RetryDelays.Length + 1;

    private readonly IServiceScopeFactory _scopes;
    private readonly DeliverySignal _signal;
    private readonly AppLinks _links;
    private readonly TimeProvider _time;
    private readonly MessageMeter _meter;
    private readonly ILogger _log;

    private readonly List<Task> _running = [];

    private DateTime _sweptAt = DateTime.MinValue;

    public DeliveryWorker(
        IServiceScopeFactory scopes,
        DeliverySignal signal,
        AppLinks links,
        TimeProvider time,
        MessageMeter meter,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        _scopes = scopes;
        _signal = signal;
        _links = links;
        _time = time;
        _meter = meter;
        _log = loggers.CreateLogger(MessageLog.Category);
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
                _log.LogWarning(exception, "A delivery poll failed. The next poll retries it.");
            }
        }

        await Task.WhenAll(_running);
    }

    private async Task SweepAsync(CancellationToken token)
    {
        if (DateTime.UtcNow - _sweptAt < SweepInterval)
        {
            return;
        }

        _sweptAt = DateTime.UtcNow;
        await using var scope = _scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeliverySweep>().RunAsync(token);
    }

    private async Task ClaimAsync(CancellationToken token)
    {
        _running.RemoveAll(running => running.IsCompleted);
        while (_running.Count < MaxSends && await ClaimOneAsync(token) is { } delivery)
        {
            _running.Add(RunAsync(delivery, token));
        }
    }

    private async Task RunAsync(Delivery delivery, CancellationToken token)
    {
        try
        {
            await SendAsync(delivery, token);
        }
        finally
        {
            _signal.Poke();
        }
    }

    private async Task<Delivery?> ClaimOneAsync(CancellationToken token)
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
            if (delivery.Attempts > MaxAttempts)
            {
                await FailAsync(database, delivery, "retriesExhausted", status: null, code: null, token);
                return;
            }

            if (await DropReasonAsync(database, delivery, token) is { } drop)
            {
                await DropAsync(database, delivery, drop, token);
                return;
            }

            var address = delivery.Address ?? await database.Users
                .Where(u => u.Id == delivery.UserId)
                .Select(u => u.Email)
                .SingleAsync(token);
            await TrySendAsync(database, scope.ServiceProvider.GetRequiredService<IEmailProvider>(), delivery, address, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "A delivery send threw. The claim's lease runs out and the next poll retries it.");
        }
    }

    private async Task<DeliveryDrop?> DropReasonAsync(
        ApplicationDbContext database, Delivery delivery, CancellationToken token)
    {
        if (_time.GetUtcNow().UtcDateTime > delivery.SendBy)
        {
            return DeliveryDrop.Expired;
        }

        if (delivery.UserId is { } userId)
        {
            var live = await database.Users.AnyAsync(u => u.Id == userId, token);
            return live ? null : DeliveryDrop.UserDeleted;
        }

        return string.IsNullOrEmpty(delivery.Address) ? DeliveryDrop.NoAddress : null;
    }

    private async Task DropAsync(ApplicationDbContext database, Delivery delivery, DeliveryDrop drop, CancellationToken token)
    {
        await EndAsync(database, delivery, DeliveryStatus.Dropped, token);
        if (drop == DeliveryDrop.Expired)
        {
            _log.DeliveryExpired(delivery.SendBy);
            _meter.Delivered(DeliveryResult.Expired, delivery.Channel);
            return;
        }

        _log.DeliveryDropped(drop == DeliveryDrop.UserDeleted ? "userDeleted" : "noAddress");
        _meter.Delivered(DeliveryResult.Dropped, delivery.Channel);
    }

    private enum DeliveryDrop
    {
        Expired,
        UserDeleted,
        NoAddress
    }

    private async Task TrySendAsync(
        ApplicationDbContext database, IEmailProvider provider, Delivery delivery, string address, CancellationToken token)
    {
        var body = EmailTemplates.Render(delivery.Type, delivery.Message, _links);
        EmailSent sent;
        try
        {
            sent = await provider.SendAsync(
                new EmailContent(address, body.Subject, EmailLayout.Html(body), EmailLayout.Text(body), delivery.Type.ToString()),
                delivery.IdempotencyKey,
                token);
        }
        catch (EmailProviderException rejected) when (rejected.Status is >= 400 and < 500 and not 429)
        {
            await FailAsync(database, delivery, "rejected", rejected.Status, rejected.Code, token);
            return;
        }
        catch (EmailProviderException answered)
        {
            await RetryAsync(
                database, delivery, answered.Status == 429 ? "rateLimited" : "serverError", answered.Status, answered.Code, token);
            return;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            await RetryAsync(database, delivery, "timeout", status: null, code: null, token);
            return;
        }
        catch (HttpRequestException)
        {
            await RetryAsync(database, delivery, "networkError", status: null, code: null, token);
            return;
        }

        await EndAsync(database, delivery, DeliveryStatus.Sent, token, sent.ProviderMessageId, address);
        _log.DeliverySent(sent.ProviderMessageId);
        _meter.Delivered(DeliveryResult.Sent, delivery.Channel);
    }

    private async Task RetryAsync(
        ApplicationDbContext database, Delivery claimed, string reason, int? status, string? code, CancellationToken token)
    {
        if (claimed.Attempts >= MaxAttempts)
        {
            await FailAsync(database, claimed, "retriesExhausted", status, code, token);
            return;
        }

        var nextAttemptAt = _time.GetUtcNow().UtcDateTime + RetryDelays[claimed.Attempts - 1];
        await database.Deliveries
            .Where(d => d.Id == claimed.Id && d.Status == DeliveryStatus.Pending && d.Attempts == claimed.Attempts)
            .ExecuteUpdateAsync(d => d.SetProperty(e => e.NextAttemptAt, nextAttemptAt), token);
        _log.DeliveryRetrying(reason, status, nextAttemptAt);
    }

    private async Task FailAsync(
        ApplicationDbContext database, Delivery claimed, string reason, int? status, string? code, CancellationToken token)
    {
        await EndAsync(database, claimed, DeliveryStatus.Failed, token);
        _log.DeliveryFailed(reason, status, code);
        _meter.Delivered(DeliveryResult.Failed, claimed.Channel);
    }

    private static Task<int> EndAsync(
        ApplicationDbContext database,
        Delivery claimed,
        DeliveryStatus status,
        CancellationToken token,
        string? providerMessageId = null,
        string? address = null) =>
        database.Deliveries
            .Where(d => d.Id == claimed.Id && d.Status == DeliveryStatus.Pending && d.Attempts == claimed.Attempts)
            .ExecuteUpdateAsync(
                d => d
                    .SetProperty(e => e.Status, status)
                    .SetProperty(e => e.ProviderMessageId, providerMessageId)
                    .SetProperty(e => e.Secret, (string?)null)
                    .SetProperty(e => e.Address, e => address ?? e.Address),
                token);
}
