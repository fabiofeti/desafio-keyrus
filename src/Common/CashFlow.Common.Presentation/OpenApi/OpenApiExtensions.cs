using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;

namespace CashFlow.Common.Presentation.OpenApi;

public static class OpenApiExtensions
{
    /// <summary>
    /// Documents a header the endpoint writes on one of its responses. The
    /// generator cannot see <c>Response.Headers[...]</c> in a handler, so the
    /// endpoint states it next to its <c>Produces</c> calls - the contract stays
    /// in the code that implements it.
    /// </summary>
    public static RouteHandlerBuilder ProducesResponseHeader(
        this RouteHandlerBuilder builder, int statusCode, string name, string description) =>
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (operation.Responses?.TryGetValue(statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture), out IOpenApiResponse? response) == true
                && response is OpenApiResponse concrete)
            {
                concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
                concrete.Headers[name] = new OpenApiHeader
                {
                    Description = description,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                };
            }

            return Task.CompletedTask;
        });
}
