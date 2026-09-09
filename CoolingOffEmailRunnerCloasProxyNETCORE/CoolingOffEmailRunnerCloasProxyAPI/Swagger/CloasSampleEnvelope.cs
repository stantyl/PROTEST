namespace CoolingOffEmailRunnerCloasProxyAPI.Swagger;

/// <summary>
/// The exact CLOAS SOAP envelope that <c>CoolingOffEmailRunnerCloasProxyConsoleTest</c>
/// builds and posts (method <c>RetrieveCoolingOffDtlsByPlan</c>, plans 1000001..1000003,
/// values taken from that project's <c>App.config</c>).
///
/// Kept byte-for-byte identical to the console test's <c>SoapEnvelopeTemplate</c>
/// output - same element order, same namespaces, same 4-space indentation - so the
/// Swagger "Try it out" body and the console test send exactly the same XML style.
/// </summary>
public static class CloasSampleEnvelope
{
    public const string Xml =
"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
"<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n" +
"    <soap:Body>\n" +
"        <RetrieveCoolingOffDtlsByPlan xmlns=\"http://ilfs/Cloas\">\n" +
"            <methodName>RetrieveCoolingOffDtlsByPlan</methodName>\n" +
"            <systemId>AZC</systemId>\n" +
"            <userId>123</userId>\n" +
"            <systemReference>CCOE</systemReference>\n" +
"            <policyRequest xmlns:dbpl=\"http://ilfs/Cloas/PolicyApi\" xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">\n" +
"                <dbpl:PolicyRequestData i:type=\"dbpl:RetrieveCoolingOffDtlsByPlan\">\n" +
"                    <dbpl:Policies xmlns:d4p1=\"http://schemas.microsoft.com/2003/10/Serialization/Arrays\">\n" +
"                        <d4p1:string>1000001</d4p1:string>\n" +
"                        <d4p1:string>1000002</d4p1:string>\n" +
"                        <d4p1:string>1000003</d4p1:string>\n" +
"                    </dbpl:Policies>\n" +
"                </dbpl:PolicyRequestData>\n" +
"            </policyRequest>\n" +
"        </RetrieveCoolingOffDtlsByPlan>\n" +
"    </soap:Body>\n" +
"</soap:Envelope>";

    /// <summary>SOAPAction header the console test sends (App.config <c>Cloas.SoapAction</c>).</summary>
    public const string SoapAction = "http://ilfs/Cloas/Policy";
}
