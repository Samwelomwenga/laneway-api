using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Laneway.Api;

// An action filter would run after model binding, so a forged request with a bad body would get a 400,
// not the 403.
[AttributeUsage(AttributeTargets.Method)]
public sealed class CsrfCheckedAttribute : TypeFilterAttribute
{
    public CsrfCheckedAttribute() : base(typeof(CsrfCheck))
    {
    }
}

public sealed class CsrfCheck(IOptions<CorsOptions> cors, AuthMeter meter, ILoggerFactory loggers) : IResourceFilter
{
    public const string Header = "X-Laneway-Csrf";

    private const string Refused = "Request refused.";

    private readonly ILogger _log = loggers.CreateLogger(AuthLog.Category);

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FailedCheck(context.HttpContext.Request) is not { } check)
        {
            return;
        }

        _log.CsrfRejected(check);
        meter.CsrfRejected(check);
        context.Result = new ObjectResult(ApiResponse<object>.ErrorResponse(
            Refused, StatusCodes.Status403Forbidden, [new ApiError(null, ErrorCodes.CsrfRejected, Refused)]))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    private FailedCsrfCheck? FailedCheck(HttpRequest request)
    {
        var headers = request.Headers;
        if (headers[Header] != "1")
        {
            return FailedCsrfCheck.Header;
        }

        if (headers.Origin.Count > 0 && !AllowedOrigin(request, headers.Origin.ToString()))
        {
            return FailedCsrfCheck.Origin;
        }

        if (string.Equals(headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            return FailedCsrfCheck.FetchSite;
        }

        return null;
    }

    private bool AllowedOrigin(HttpRequest request, string origin) =>
        string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase)
        || cors.Value.GetPolicy(cors.Value.DefaultPolicyName)?.IsOriginAllowed(origin) == true;
}

public enum FailedCsrfCheck
{
    Header,
    Origin,
    FetchSite
}
