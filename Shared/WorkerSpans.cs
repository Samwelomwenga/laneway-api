using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Laneway.Api;

public static class WorkerSpans
{
    public const string MessageSource = MessageMeter.Name;

    public const string CopyJobSource = "Laneway.CopyJob";

    public const string WorkerSource = "Laneway.Worker";

    public static ActivitySource Message { get; } = new(MessageSource);

    public static ActivitySource CopyJob { get; } = new(CopyJobSource);

    public static ActivitySource Worker { get; } = new(WorkerSource);

    public static string[] Sources { get; } = [MessageSource, CopyJobSource, WorkerSource];

    /// <summary>
    /// The current span as a W3C <c>traceparent</c>, for a row a worker picks up later.
    /// </summary>
    public static string? Origin() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.Id : null;

    /// <summary>
    /// Starts a span with no parent, so a worker's run is its own trace. A link to <paramref name="origin"/>
    /// leads back to the request that queued the work without stretching that request's trace across retries.
    /// </summary>
    public static Activity? StartRoot(this ActivitySource source, string name, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        Activity.Current = null;
        ActivityLink[]? links = ActivityContext.TryParse(origin, null, out var context) ? [new ActivityLink(context)] : null;
        return source.StartActivity(name, ActivityKind.Internal, parentContext: default, links: links);
    }
}

internal static class ParentlessClientDrop
{
    public static Sampler Sampler { get; } = new ParentBasedSampler(new ClientRootDrop());

    private sealed class ClientRootDrop : Sampler
    {
        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
            new(samplingParameters.Kind == ActivityKind.Client ? SamplingDecision.Drop : SamplingDecision.RecordAndSample);
    }
}
