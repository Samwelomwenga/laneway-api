using System.Diagnostics;
using OpenTelemetry;

namespace Laneway.Api;

internal sealed class ProbeTraceDrop : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Status != ActivityStatusCode.Error && IsProbe(LocalRoot(data)))
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    private static Activity LocalRoot(Activity activity)
    {
        while (activity.Parent is { } parent)
        {
            activity = parent;
        }

        return activity;
    }

    private static bool IsProbe(Activity root) =>
        root.Kind == ActivityKind.Server && root.GetTagItem("url.path") is string path && HealthProbes.IsProbe(path);
}
