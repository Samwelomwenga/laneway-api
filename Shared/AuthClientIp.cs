using Serilog.Core;
using Serilog.Events;

namespace Laneway.Api;

internal sealed class AuthClientIp(IHttpContextAccessor requests) : ILogEventEnricher
{
    private static readonly ScalarValue AuthCategory = new(AuthLog.Category);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        if (logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var category)
            && AuthCategory.Equals(category)
            && ClientIp.Of(requests.HttpContext) is { } address)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(ClientIp.Field, address));
        }
    }
}
