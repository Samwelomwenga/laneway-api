using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace Laneway.Api;

// A request with no Content-Type passes every [Consumes] constraint, so two creates that share a route
// come back as an ambiguous match, which is a 500. This keeps its action out of that race and leaves
// the other one to answer. The order puts it in the same round as [Consumes]. That round matters,
// because the [Consumes] check reads every candidate left in it to pick the one that returns the 415.
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresContentTypeAttribute : Attribute, IActionConstraint
{
    public int Order => ConsumesAttribute.ConsumesActionConstraintOrder;

    public bool Accept(ActionConstraintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.RouteContext.HttpContext.Request.ContentType is not null;
    }
}
