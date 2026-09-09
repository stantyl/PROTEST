using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CoolingOffEmailRunnerCloasProxyAPI.Swagger;

public sealed class RawXmlBodyOperationFilter : IOperationFilter
{
    // Exactly the envelope CoolingOffEmailRunnerCloasProxyConsoleTest posts.
    private const string SampleEnvelope = CloasSampleEnvelope.Xml;

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAttribute = context.MethodInfo.GetCustomAttributes(true)
            .OfType<RawXmlBodyAttribute>().Any();
        if (!hasAttribute)
        {
            return;
        }

        var example = new OpenApiString(SampleEnvelope);

        // Set the example on BOTH the media type and the schema, and also as the
        // schema default: Swagger UI only pre-fills the "Try it out" editor for a
        // non-JSON body from the schema (example/default), not from the media-type
        // example alone - so without this the XML text area comes up blank.
        var mediaType = new OpenApiMediaType
        {
            Schema = new OpenApiSchema
            {
                Type = "string",
                Format = "xml",
                Example = example,
                Default = example
            },
            Example = example
        };

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description =
                "Raw CLOAS SOAP envelope - identical in style to what " +
                "CoolingOffEmailRunnerCloasProxyConsoleTest sends. Forwarded unchanged to CLOAS.",
            Content =
            {
                ["text/xml"] = mediaType,
                ["application/xml"] = mediaType,
                ["application/soap+xml"] = mediaType
            }
        };

        operation.Parameters ??= new List<OpenApiParameter>();
        if (!operation.Parameters.Any(p => string.Equals(p.Name, "SOAPAction", StringComparison.OrdinalIgnoreCase)))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "SOAPAction",
                In = ParameterLocation.Header,
                Required = false,
                Description = "SOAPAction header, forwarded unchanged to CLOAS.",
                Schema = new OpenApiSchema
                {
                    Type = "string",
                    Default = new OpenApiString(CloasSampleEnvelope.SoapAction)
                },
                Example = new OpenApiString(CloasSampleEnvelope.SoapAction)
            });
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RawXmlBodyAttribute : Attribute;
