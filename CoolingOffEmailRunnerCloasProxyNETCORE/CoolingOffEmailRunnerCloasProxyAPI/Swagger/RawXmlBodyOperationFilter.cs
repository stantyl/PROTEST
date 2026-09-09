using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CoolingOffEmailRunnerCloasProxyAPI.Swagger;

public sealed class RawXmlBodyOperationFilter : IOperationFilter
{
    private const string SampleEnvelope =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <soap:Body>
    <RetrieveCoolingOffDtlsByPlan xmlns=""http://ilfs/Cloas"">
      <methodName>RetrieveCoolingOffDtlsByPlan</methodName>
      <systemId>AZC</systemId>
      <userId>123</userId>
      <systemReference>CCOE</systemReference>
      <policyRequest xmlns:dbpl=""http://ilfs/Cloas/PolicyApi"" xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"">
        <dbpl:PolicyRequestData i:type=""dbpl:RetrieveCoolingOffDtlsByPlan"">
          <dbpl:Policies xmlns:d4p1=""http://schemas.microsoft.com/2003/10/Serialization/Arrays"">
            <d4p1:string>1000001</d4p1:string>
            <d4p1:string>1000002</d4p1:string>
            <d4p1:string>1000003</d4p1:string>
          </dbpl:Policies>
        </dbpl:PolicyRequestData>
      </policyRequest>
    </RetrieveCoolingOffDtlsByPlan>
  </soap:Body>
</soap:Envelope>";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAttribute = context.MethodInfo.GetCustomAttributes(true)
            .OfType<RawXmlBodyAttribute>().Any();
        if (!hasAttribute)
        {
            return;
        }

        var mediaType = new OpenApiMediaType
        {
            Schema = new OpenApiSchema { Type = "string", Format = "xml" },
            Example = new OpenApiString(SampleEnvelope)
        };

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Raw CLOAS SOAP envelope.",
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
                Description = "SOAPAction header, forwarded unchanged to CLOAS (e.g. http://ilfs/Cloas/Policy).",
                Schema = new OpenApiSchema { Type = "string" },
                Example = new OpenApiString("http://ilfs/Cloas/Policy")
            });
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RawXmlBodyAttribute : Attribute;
