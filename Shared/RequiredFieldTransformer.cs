using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Laneway.Api;

public sealed class RequiredFieldTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var schemas = document.Components?.Schemas;
        if (schemas is null)
        {
            return Task.CompletedTask;
        }

        var bodies = document.Paths.Values
            .SelectMany(path => path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            .SelectMany(operation => operation.RequestBody?.Content?.Values ?? Enumerable.Empty<OpenApiMediaType>())
            .Select(content => content.Schema)
            .OfType<OpenApiSchemaReference>()
            .Select(reference => reference.Reference.Id)
            .OfType<string>()
            .Distinct()
            .Select(id => schemas.TryGetValue(id, out var schema) ? schema : null)
            .OfType<OpenApiSchema>();

        foreach (var body in bodies)
        {
            if (body.Required is null || body.Properties is null)
            {
                continue;
            }

            foreach (var name in body.Required)
            {
                if (body.Properties.TryGetValue(name, out var property))
                {
                    body.Properties[name] = NonNull(property);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static IOpenApiSchema NonNull(IOpenApiSchema property)
    {
        if (property.OneOf is { Count: 2 } oneOf && oneOf.Count(IsNull) == 1)
        {
            return oneOf.Single(member => !IsNull(member));
        }

        if (property.Type is { } type && type.HasFlag(JsonSchemaType.Null) && property.CreateShallowCopy() is OpenApiSchema copy)
        {
            copy.Type = type & ~JsonSchemaType.Null;
            return copy;
        }

        return property;
    }

    private static bool IsNull(IOpenApiSchema schema) => schema.Type == JsonSchemaType.Null;
}
