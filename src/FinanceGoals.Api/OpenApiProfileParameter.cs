using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FinanceGoals.Api;

/// <summary>
/// The household endpoints take their profile from the route, and the group's filter checks the
/// caller's membership, so the handlers don't declare <c>profileId</c> themselves. Without it the
/// OpenAPI document (and the generated client) would lack the path parameter, so this adds it.
/// </summary>
internal static class OpenApiProfileParameter
{
    public static OpenApiOptions AddProfileIdParameter(this OpenApiOptions options) =>
        options.AddOperationTransformer((operation, context, _) =>
        {
            var route = context.Description.RelativePath ?? string.Empty;
            var declared = operation.Parameters?.Any(p => p.Name == "profileId") == true;
            if (route.Contains("{profileId", StringComparison.Ordinal) && !declared)
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "profileId",
                    In = ParameterLocation.Path,
                    Required = true,
                    Description = "The household profile; the caller must be a member (404 otherwise).",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                });
            }

            return Task.CompletedTask;
        });
}
