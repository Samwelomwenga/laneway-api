using Serilog.Core;
using Serilog.Events;

namespace Laneway.Api;

internal sealed class RequestScopeScrub : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        logEvent.RemovePropertyIfPresent("RequestPath");
        logEvent.RemovePropertyIfPresent("RequestId");
    }
}
