using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DefaultNamespace;

[AttributeUsage(AttributeTargets.Method)]
public sealed class NoActorAttribute : Attribute;

public sealed class ActorFilter : IAsyncResourceFilter
{
    public const string HeaderName = "X-User-Id";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!NeedsActor(context))
        {
            await next();
            return;
        }

        if (await ResolveAsync(context.HttpContext) is { } error)
        {
            context.Result = InvalidDataResult.Create([error]);
            return;
        }

        await next();
    }

    private static bool NeedsActor(ResourceExecutingContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsPost(method) && !HttpMethods.IsPut(method) && !HttpMethods.IsDelete(method))
        {
            return false;
        }

        return !context.ActionDescriptor.EndpointMetadata.OfType<NoActorAttribute>().Any();
    }

    private static async Task<ApiError?> ResolveAsync(HttpContext context)
    {
        var header = context.Request.Headers[HeaderName];
        if (header.Count == 0 || string.IsNullOrWhiteSpace(header[0]))
        {
            return new ApiError(HeaderName, ErrorCodes.Required, $"'{HeaderName}' is required.");
        }

        if (header.Count > 1 || !Guid.TryParse(header[0], out var id))
        {
            return new ApiError(HeaderName, ErrorCodes.InvalidFormat, $"'{HeaderName}' isn't in a valid format.");
        }

        var database = context.RequestServices.GetRequiredService<ApplicationDbContext>();
        if (!await database.Users.AnyAsync(user => user.Id == id, context.RequestAborted))
        {
            return new ApiError(HeaderName, ErrorCodes.NotFound, $"User {id} does not exist.");
        }

        context.RequestServices.GetRequiredService<Actor>().Resolve(id);
        return null;
    }
}
