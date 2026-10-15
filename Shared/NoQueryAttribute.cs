using Microsoft.AspNetCore.Mvc.Filters;

namespace Laneway.Api;

[AttributeUsage(AttributeTargets.Method)]
public sealed class NoQueryAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var errors = new QueryReader(context.HttpContext.Request.Query).Errors();
        if (errors.Count > 0)
        {
            context.Result = InvalidDataResult.Create(errors);
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
