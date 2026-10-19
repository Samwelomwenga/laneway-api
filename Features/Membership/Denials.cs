using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace Laneway.Api;

public enum AccessTarget
{
    Workspace,
    Board,
    List,
    Card,
    Label,
    Checklist,
    CheckItem,
    Attachment,
    Comment
}

public static class AccessTargets
{
    public static string Name(AccessTarget target) => target switch
    {
        AccessTarget.CheckItem => "Check item",
        _ => target.ToString()
    };
}

public sealed record Denial(Guid ActorId, AccessTarget Target, Guid TargetId, string? Code)
{
    public bool IsHidingNotFound => Code is null;
}

public sealed class DenialMeter
{
    public const string Name = "Laneway.Authorization";

    public const string DroppedName = "laneway.authorization.denials.dropped";

    private readonly Counter<long> _dropped;

    public DenialMeter(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _dropped = factory.Create(Name).CreateCounter<long>(DroppedName);
    }

    public void Dropped() => _dropped.Add(1);
}

public sealed class DenialLog
{
    public const int DefaultCapacity = 1024;

    private readonly Channel<Denial> _channel;
    private readonly DenialMeter _meter;

    public DenialLog(DenialMeter meter, int capacity = DefaultCapacity)
    {
        _meter = meter;
        _channel = Channel.CreateBounded<Denial>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    public ChannelReader<Denial> Reader => _channel.Reader;

    public void Add(Denial denial)
    {
        if (!_channel.Writer.TryWrite(denial))
        {
            _meter.Dropped();
        }
    }
}

public sealed class DenialDrainer : BackgroundService
{
    public const string Category = "Laneway.Authorization";

    private static readonly Action<ILogger, Guid, AccessTarget, Guid, int, string, Exception?> AccessDenied =
        LoggerMessage.Define<Guid, AccessTarget, Guid, int, string>(
            LogLevel.Warning,
            new EventId(1, "AccessDenied"),
            "Access denied: sub {sub}, targetType {targetType}, targetId {targetId}, status {status}, code {code}");

    private readonly DenialLog _denials;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger _logger;

    public DenialDrainer(DenialLog denials, IServiceScopeFactory scopes, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        _denials = denials;
        _scopes = scopes;
        _logger = loggers.CreateLogger(Category);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var denial in _denials.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DrainAsync(denial, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "A denial couldn't be checked, and went unlogged.");
            }
        }
    }

    private async Task DrainAsync(Denial denial, CancellationToken token)
    {
        if (denial.IsHidingNotFound && !await ExistsAsync(denial, token))
        {
            return;
        }

        AccessDenied(
            _logger,
            denial.ActorId,
            denial.Target,
            denial.TargetId,
            denial.IsHidingNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status403Forbidden,
            denial.Code ?? ErrorCodes.NotFound,
            null);
    }

    private async Task<bool> ExistsAsync(Denial denial, CancellationToken token)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return denial.Target switch
        {
            AccessTarget.Workspace => await context.WorkSpaces.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.Board => await context.Boards.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.List => await context.Lists.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.Card => await context.Cards.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.Label => await context.Labels.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.Checklist => await context.Checklists.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.CheckItem => await context.CheckItems.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.Attachment => await context.Attachments.AnyAsync(found => found.Id == denial.TargetId, token),
            AccessTarget.Comment => await context.ActivityEntries.AnyAsync(
                found => found.Id == denial.TargetId && found.Type == ActivityType.Comment, token),
            _ => throw new ArgumentOutOfRangeException(nameof(denial), denial.Target, null)
        };
    }
}
