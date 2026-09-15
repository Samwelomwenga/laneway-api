using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace DefaultNamespace;

public static partial class InvalidModelStateResponse
{
    public static IActionResult Create(ActionContext context)
    {
        var bodyType = context.ActionDescriptor.Parameters
            .FirstOrDefault(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            ?.ParameterType;
        var serializerOptions = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

        var errors = context.ModelState
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                ToApiError(entry.Key, entry.Value, error, bodyType, serializerOptions)))
            .ToList();

        return InvalidDataResult.Create(errors);
    }

    private static ApiError ToApiError(
        string key, ModelStateEntry entry, ModelError error, Type? bodyType, JsonSerializerOptions serializerOptions)
    {
        if (!key.StartsWith('$'))
        {
            return FromBindingKey(key, entry);
        }

        var match = JsonPathPattern().Match(key);
        if (!match.Success)
        {
            return new ApiError(null, ErrorCodes.InvalidFormat, "The request body isn't a valid JSON object.");
        }

        var name = match.Groups["name"].Value;
        var field = name + match.Groups["rest"].Value;

        if (error.Exception?.InnerException is null && bodyType is not null)
        {
            var property = FindProperty(serializerOptions.GetTypeInfo(bodyType), name, serializerOptions);
            if (property is null)
            {
                return new ApiError(field, ErrorCodes.UnknownField, $"'{field}' isn't a field on this request.");
            }

            var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (valueType.IsEnum)
            {
                return new ApiError(field, ErrorCodes.UnknownValue,
                    $"'{field}' must be one of: {string.Join(", ", Enum.GetNames(valueType))}.");
            }
        }

        return new ApiError(field, ErrorCodes.InvalidFormat, $"'{field}' isn't in a valid format.");
    }

    private static ApiError FromBindingKey(string key, ModelStateEntry entry)
    {
        if (key.Length == 0)
        {
            return new ApiError(null, ErrorCodes.Required, "A request body is required.");
        }

        var field = JsonNamingPolicy.CamelCase.ConvertName(key);
        return entry.RawValue is null
            ? new ApiError(field, ErrorCodes.Required, $"'{field}' is required.")
            : new ApiError(field, ErrorCodes.InvalidFormat, $"'{field}' isn't in a valid format.");
    }

    private static JsonPropertyInfo? FindProperty(JsonTypeInfo typeInfo, string name, JsonSerializerOptions options)
    {
        var comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return typeInfo.Properties.FirstOrDefault(property => string.Equals(property.Name, name, comparison));
    }

    [GeneratedRegex(@"^\$(?:\.(?<name>[^.\[]+)|\['(?<name>[^']*)'\])(?<rest>.*)$")]
    private static partial Regex JsonPathPattern();
}
