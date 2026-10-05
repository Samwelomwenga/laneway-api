using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Laneway.Api;

public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new List<ApiError>();
        foreach (var argument in context.ActionArguments.Values.OfType<object>())
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            errors.AddRange(result.Errors.Select(failure =>
                new ApiError(ToFieldPath(failure.PropertyName), failure.ErrorCode, failure.ErrorMessage)));
        }

        if (errors.Count > 0)
        {
            context.Result = InvalidDataResult.Create(errors);
            return;
        }

        await next();
    }

    private static string? ToFieldPath(string propertyName) =>
        propertyName.Length == 0
            ? null
            : string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
