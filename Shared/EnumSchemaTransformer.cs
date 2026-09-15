using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace DefaultNamespace;

public sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type.IsEnum)
        {
            schema.Type = "string";
            schema.Enum = Enum.GetNames(type).Select(IOpenApiAny (name) => new OpenApiString(name)).ToList();
        }

        return Task.CompletedTask;
    }
}
