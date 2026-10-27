using Serilog.Core;
using Serilog.Events;

namespace Laneway.Api;

internal sealed class CategoryClientIp(IHttpContextAccessor requests) : ILogEventEnricher
{
    private static readonly ScalarValue[] Categories = [new(AuthLog.Category), new(AccessLog.Category)];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        if (logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var category)
            && Categories.Contains(category)
            && ClientIp.Of(requests.HttpContext) is { } address)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(ClientIp.Field, address));
        }
    }
}
