using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParcelAPI.Filters
{
    public class AddClientIdHeaderParameter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            operation.Parameters ??= [];

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-Client-Identifier",
                In = ParameterLocation.Header,
                Description = "Client identifier for tracking requests",
                Required = false,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                }
            });
        }
    }
}
