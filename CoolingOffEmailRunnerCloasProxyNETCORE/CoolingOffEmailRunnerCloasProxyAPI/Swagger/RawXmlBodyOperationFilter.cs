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

        // IMPORTANT: hand Swagger UI the sample through `examples` (plural), NOT
        // `example`. For an xml media type Swagger UI ignores `example` and tries
        // to *generate* an XML sample from the schema (here just `type: string`),
        // which fails with "Example cannot be generated; root element name is
        // undefined". A named entry under `examples` is rendered verbatim and
        // pre-fills the "Try it out" body editor, so you can just hit Execute.
        OpenApiMediaType NewMediaType() => new()
        {
            Schema = new OpenApiSchema { Type = "string", Format = "xml" },
            Examples =
            {
                ["ConsoleTestEnvelope"] = new OpenApiExample
                {
                    Summary = "Same envelope CoolingOffEmailRunnerCloasProxyConsoleTest sends",
                    Value = example
                }
            }
        };

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description =
                "Raw CLOAS SOAP envelope - identical in style to what " +
                "CoolingOffEmailRunnerCloasProxyConsoleTest sends. Forwarded unchanged to CLOAS.",
            Content =
            {
                ["text/xml"] = NewMediaType(),
                ["application/xml"] = NewMediaType(),
                ["application/soap+xml"] = NewMediaType()
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
