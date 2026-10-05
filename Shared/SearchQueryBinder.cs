using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DefaultNamespace;

public interface ISearchQuery<TSelf> where TSelf : ISearchQuery<TSelf>
{
    static abstract TSelf Read(QueryReader query);
}

public sealed class SearchQueryBinder<TQuery> : IModelBinder where TQuery : ISearchQuery<TQuery>
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var query = new QueryReader(bindingContext.HttpContext.Request.Query);
        var search = TQuery.Read(query);
        var errors = query.Errors();
        if (errors.Count > 0)
        {
            SearchQueryErrors.Record(bindingContext.HttpContext, errors);
            bindingContext.ModelState.AddModelError(bindingContext.ModelName, "The query string isn't valid.");
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(search);
        return Task.CompletedTask;
    }
}

public static class SearchQueryErrors
{
    private const string ItemKey = "SearchQueryErrors";

    public static void Record(HttpContext context, List<ApiError> errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[ItemKey] = errors;
    }

    public static List<ApiError>? Recorded(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items[ItemKey] as List<ApiError>;
    }
}
