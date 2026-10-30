using System.Diagnostics;
using OpenTelemetry;

namespace Laneway.Api;

internal sealed class SpanScrub : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        data.SetTag("user_agent.original", null);
        data.SetTag("db.npgsql.data_source", null);
        data.SetTag("url.path", null);
        data.SetTag("url.query", null);
        if (data.GetTagItem("url.full") is string url && url.StartsWith(BreachedPasswords.RangeUrl, StringComparison.Ordinal))
        {
            data.SetTag("url.full", BreachedPasswords.RangeTemplate);
        }

        data.SetTag("otel.status_description", null);
        data.SetStatus(data.Status, description: null);
    }
}
