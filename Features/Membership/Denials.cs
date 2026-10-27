using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    Comment,
    CopyJob
}

public static class AccessTargets
{
    public static string Name(AccessTarget target) => target switch
    {
        AccessTarget.CheckItem => "Check item",
        AccessTarget.CopyJob => "Copy job",
        _ => target.ToString()
    };
}

public sealed record Denial(
    Guid ActorId,
    AccessTarget Target,
    Guid TargetId,
    string? Code,
    bool FromField = false,
    string? ClientIp = null,
    ActivityContext Trace = default)
{
    public bool IsHidingNotFound => Code is null;

    public int Status => Code is not null ? StatusCodes.Status403Forbidden
        : FromField ? StatusCodes.Status400BadRequest
        : StatusCodes.Status404NotFound;
}

public sealed class DenialMeter
{
    public const string Name = "Laneway.Authorization";

    public const string DroppedName = "laneway.authorization.denials.dropped";

    public const string UnknownTargetName = "laneway.authorization.denials.unknown_target";

    private readonly Counter<long> _dropped;
    private readonly Counter<long> _unknownTarget;

    public DenialMeter(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var meter = factory.Create(Name);
        _dropped = meter.CreateCounter<long>(DroppedName);
        _unknownTarget = meter.CreateCounter<long>(UnknownTargetName);
    }

    public void Dropped() => _dropped.Add(1);

    public void UnknownTarget(AccessTarget target) =>
        _unknownTarget.Add(1, new KeyValuePair<string, object?>("targetType", target.ToString()));
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

[SuppressMessage("Usage", "SYSLIB1025", Justification = "Variants of one event share its name.")]
public sealed partial class DenialDrainer : BackgroundService
{
    public const string Category = "Laneway.Authorization";

    [LoggerMessage(EventId = 1, EventName = "AccessDenied", Level = LogLevel.Warning,
        Message = "Access denied: sub {sub}, targetType {targetType}, targetId {targetId}, status {status}, code {code}")]
    private static partial void AccessDenied(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, int status, string code);

    [LoggerMessage(EventId = 1, EventName = "AccessDenied", Level = LogLevel.Warning,
        Message = "Access denied: sub {sub}, targetType {targetType}, targetId {targetId}, status {status}, code {code}, clientIp {clientIp}")]
    private static partial void AccessDenied(
        ILogger logger, Guid sub, AccessTarget targetType, Guid targetId, int status, string code, string clientIp);

    [LoggerMessage(EventId = 2, EventName = "DenialCheckFailed", Level = LogLevel.Warning,
        Message = "A denial couldn't be checked, and went unlogged.")]
    private static partial void DenialCheckFailed(ILogger logger, Exception exception);

    private readonly DenialLog _denials;
    private readonly DenialMeter _meter;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger _logger;

    public DenialDrainer(DenialLog denials, DenialMeter meter, IServiceScopeFactory scopes, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        _denials = denials;
        _meter = meter;
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
                DenialCheckFailed(_logger, exception);
            }
        }
    }

    private async Task DrainAsync(Denial denial, CancellationToken token)
    {
        if (denial.IsHidingNotFound && !await ExistsAsync(denial, token))
        {
            _meter.UnknownTarget(denial.Target);
            return;
        }

        var code = denial.Code ?? ErrorCodes.NotFound;
        using var trace = Rejoin(denial.Trace);
        if (denial.ClientIp is { } clientIp)
        {
            AccessDenied(_logger, denial.ActorId, denial.Target, denial.TargetId, denial.Status, code, clientIp);
        }
        else
        {
            AccessDenied(_logger, denial.ActorId, denial.Target, denial.TargetId, denial.Status, code);
        }
    }

    // No ActivitySource the tracer listens to starts this Activity, so the log joins the request's trace and no
    // span ships.
    private static Activity? Rejoin(ActivityContext trace)
    {
        if (trace == default)
        {
            return null;
        }

        var activity = new Activity(nameof(AccessDenied));
        activity.SetParentId(trace.TraceId, trace.SpanId, trace.TraceFlags);
        activity.Start();
        return activity;
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
            AccessTarget.CopyJob => await context.CopyJobs.AnyAsync(found => found.Id == denial.TargetId, token),
            _ => throw new ArgumentOutOfRangeException(nameof(denial), denial.Target, null)
        };
    }
}
