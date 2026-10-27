using System.Collections.Frozen;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Laneway.Api;

public static class RequestLine
{
    public const string Category = "Laneway.Request";

    public const string EventName = "RequestCompleted";

    public const string Unmatched = "unmatched";

    public const string OtherMethod = "other";

    public const string ErrorCodesField = "errorCodes";

    private static readonly object ErrorCodesKey = new();

    private const double SlowMs = 1000;

    private static readonly FrozenSet<string> Methods = FrozenSet.ToFrozenSet(
    [
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch,
        HttpMethods.Delete, HttpMethods.Options, HttpMethods.Trace, HttpMethods.Connect
    ], StringComparer.Ordinal);

    private static readonly LogEventProperty SourceContext = new(Constants.SourceContextPropertyName, new ScalarValue(Category));

    private static readonly LogEventProperty EventId = new("EventId", new StructureValue(
    [
        new LogEventProperty("Id", new ScalarValue(1)),
        new LogEventProperty("Name", new ScalarValue(EventName))
    ]));

    public static IApplicationBuilder UseRequestLine(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            // The middleware stamps its own type as SourceContext over any logger it gets, so the category goes on
            // the event instead. A level override for this line keys on Serilog.AspNetCore.RequestLoggingMiddleware.
            options.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
            options.MessageTemplate = "Request completed: {RequestMethod:l} {route:l}, status {StatusCode} in {Elapsed:0.0} ms";
            options.GetLevel = Level;
            options.GetMessageTemplateProperties = (context, _, elapsed, status) =>
            [
                SourceContext,
                EventId,
                new LogEventProperty("RequestMethod", new ScalarValue(Method(context.Request.Method))),
                new LogEventProperty("route", new ScalarValue(Route(context))),
                new LogEventProperty("StatusCode", new ScalarValue(status)),
                new LogEventProperty("Elapsed", new ScalarValue(elapsed))
            ];
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                if (ClientIp.Of(context) is { } address)
                {
                    diagnostics.Set(ClientIp.Field, address);
                }

                if (ErrorCodesOf(context) is { } codes)
                {
                    diagnostics.Set(ErrorCodesField, codes);
                }

                if (context.RequestServices.GetService<Actor>() is { IsResolved: true, HasSession: true } actor)
                {
                    diagnostics.Set("sub", actor.Id);
                    diagnostics.Set("sid", actor.SessionId);
                }
            };
        });

    public static void Note(HttpContext context, IEnumerable<ApiError> errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);

        if (context.Items[ErrorCodesKey] is not SortedSet<string> codes)
        {
            codes = new SortedSet<string>(StringComparer.Ordinal);
            context.Items[ErrorCodesKey] = codes;
        }

        codes.UnionWith(errors.Select(error => error.Code).Where(ErrorCodes.All.Contains));
    }

    public static Task WriteApiResponseAsync<T>(
        this HttpResponse response, ApiResponse<T> body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(body);

        Note(response.HttpContext, body.Errors);
        return response.WriteAsJsonAsync(body, cancellationToken);
    }

    private static string[]? ErrorCodesOf(HttpContext context) =>
        context.Response.StatusCode is >= 400 and < 500
        && context.Items[ErrorCodesKey] is SortedSet<string> { Count: > 0 } codes
            ? [.. codes]
            : null;

    private static LogEventLevel Level(HttpContext context, double elapsedMs, Exception? exception) =>
        exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : elapsedMs > SlowMs
                ? LogEventLevel.Warning
                : LogEventLevel.Information;

    private static string Method(string method) =>
        Methods.TryGetValue(HttpMethods.GetCanonicalizedValue(method), out var known) ? known : OtherMethod;

    // ExceptionHandlerMiddleware clears the endpoint before it runs the handler, and keeps it on its feature.
    private static string Route(HttpContext context) =>
        (context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint) is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? Unmatched
            : Unmatched;
}

public sealed class ErrorCodesFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Result is ObjectResult { Value: IApiResponse response })
        {
            RequestLine.Note(context.HttpContext, response.Errors);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
