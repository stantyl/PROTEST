using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CoolingOffEmailRunnerCloasProxyAPI;
using CoolingOffEmailRunnerCloasProxyAPI.Swagger;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoolingOffEmailRunnerCloasProxyAPI.Controllers;

[ApiController]
[Route("api/cloas")]
public sealed class CloasProxyController : ControllerBase
{
    public const string HttpClientName = "cloas";

    private const long MaxBodyBytes = 52_428_800;

    // ---------------------------------------------------------------------
    // TEMPORARY TEST STUB DATA - self-contained, does NOT depend on Swagger.
    // Known-good CLOAS request (method RetrieveCoolingOffDtlsByPlan, plans
    // 1000001..1000003) - same style the .NET 4.5.2 console test posts.
    // Delete this, plus the matching block in Process(), once a real request works.
    private const string TestStubSoapAction = "http://ilfs/Cloas/Policy";

    private const string TestStubEnvelope =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
            <soap:Body>
                <RetrieveCoolingOffDtlsByPlan xmlns="http://ilfs/Cloas">
                    <methodName>RetrieveCoolingOffDtlsByPlan</methodName>
                    <systemId>AZC</systemId>
                    <userId>123</userId>
                    <systemReference>CCOE</systemReference>
                    <policyRequest xmlns:dbpl="http://ilfs/Cloas/PolicyApi" xmlns:i="http://www.w3.org/2001/XMLSchema-instance">
                        <dbpl:PolicyRequestData i:type="dbpl:RetrieveCoolingOffDtlsByPlan">
                            <dbpl:Policies xmlns:d4p1="http://schemas.microsoft.com/2003/10/Serialization/Arrays">
                                <d4p1:string>1000001</d4p1:string>
                                <d4p1:string>1000002</d4p1:string>
                                <d4p1:string>1000003</d4p1:string>
                            </dbpl:Policies>
                        </dbpl:PolicyRequestData>
                    </policyRequest>
                </RetrieveCoolingOffDtlsByPlan>
            </soap:Body>
        </soap:Envelope>
        """;
    // ------------------------- end test stub data -------------------------

    private static readonly ILog Log = LogManager.GetLogger(typeof(CloasProxyController));

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloasProxyOptions _options;

    public CloasProxyController(IHttpClientFactory httpClientFactory, IOptions<CloasProxyOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    [HttpPost]
    [RawXmlBody]
    [RequestSizeLimit(MaxBodyBytes)]
    // No [Consumes]: this is a raw pass-through proxy that reads Request.Body
    // directly (no [FromBody] binding), so it must accept whatever Content-Type
    // the caller sends and forward it unchanged - exactly like the old WCF
    // endpoint. A [Consumes] list makes MVC reject any other media type with
    // 415 Unsupported Media Type *before* the action runs.
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Process(CancellationToken cancellationToken)
    {
        LogIncomingHeaders();

        var soapAction = FirstHeaderValue("SOAPAction");
        var contentType = Request.ContentType;
        if (string.IsNullOrEmpty(contentType))
        {
            contentType = "text/xml; charset=utf-8";
        }

        var targetUrl = _options.TargetServiceUrl;
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            Log.Error("CloasProxy:TargetServiceUrl is not configured in appsettings.json.");
            return await WriteErrorAsync(HttpStatusCode.InternalServerError,
                "CLOAS proxy is not configured (missing CloasProxy:TargetServiceUrl).", cancellationToken);
        }

        byte[] requestBytes;
        using (var buffer = new MemoryStream())
        {
            await Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            requestBytes = buffer.ToArray();
        }

        // ---------------------------------------------------------------------
        // TEMPORARY TEST STUB - remove / comment out the whole block below (and
        // the TestStubEnvelope / TestStubSoapAction constants above) once you
        // have confirmed a real request works.
        //
        // When the request body is empty, substitute the hard-coded known-good
        // CLOAS SOAP envelope and SOAPAction. So you can just do:
        //     POST /api/cloas   with NO body and NO headers
        // and the proxy forwards the working message. Copy the XML that shows up
        // in the log (the "--- request body ---" section) to reuse it later.
        if (requestBytes.Length == 0)
        {
            requestBytes = Encoding.UTF8.GetBytes(TestStubEnvelope);
            contentType = "text/xml; charset=utf-8";
            soapAction = TestStubSoapAction;
            Log.Warn("TEST STUB ACTIVE: request body was empty, forwarding the built-in " +
                     "hard-coded sample envelope instead.");
        }
        // --------------------------- end test stub ----------------------------

        var requestText = DecodeForLog(requestBytes, contentType);

        var stopwatch = Stopwatch.StartNew();
        Log.InfoFormat("Forwarding CLOAS request to {0} ({1} bytes, Content-Type: {2}, SOAPAction: {3})",
            targetUrl, requestBytes.Length, contentType, soapAction ?? "(none)");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            using var content = new ByteArrayContent(requestBytes);

            content.Headers.Remove("Content-Type");
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            if (!string.IsNullOrEmpty(soapAction))
            {
                request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);
            }

            request.Content = content;

            LogOutgoingRequest(request, requestText);

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var responseContentType =
                response.Content.Headers.ContentType?.ToString() ?? "text/xml; charset=utf-8";

            Log.InfoFormat("CLOAS target {0} responded {1} in {2}ms ({3} bytes)",
                targetUrl, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, responseBytes.Length);
            LogResponse(response, DecodeForLog(responseBytes, responseContentType));

            Response.StatusCode = (int)response.StatusCode;
            Response.ContentType = responseContentType;
            await Response.Body.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error($"Error forwarding CLOAS request to {targetUrl} after {stopwatch.ElapsedMilliseconds}ms", ex);
            return await WriteErrorAsync(HttpStatusCode.BadGateway,
                "CLOAS proxy error: " + ex.Message, cancellationToken);
        }
    }

    private string? FirstHeaderValue(string name) =>
        Request.Headers.TryGetValue(name, out var values) && values.Count > 0
            ? values.ToString()
            : null;

    private void LogIncomingHeaders()
    {
        var dump = string.Join(Environment.NewLine,
            Request.Headers.Select(h => "    " + h.Key + ": " + h.Value.ToString()));
        Log.InfoFormat("Incoming headers:{0}{1}", Environment.NewLine,
            string.IsNullOrEmpty(dump) ? "    (no headers found)" : dump);
    }

    private static void LogOutgoingRequest(HttpRequestMessage request, string bodyText)
    {
        Log.InfoFormat(
            "Outgoing request to CLOAS:{0}    {1} {2}{0}{3}{0}--- request body ({4} chars) ---{0}{5}{0}--- end request body ---",
            Environment.NewLine,
            request.Method, request.RequestUri,
            FormatHeaders(request.Headers, request.Content?.Headers),
            bodyText.Length,
            bodyText);
    }

    private static void LogResponse(HttpResponseMessage response, string bodyText)
    {
        Log.InfoFormat(
            "Response from CLOAS:{0}    {1} {2}{0}{3}{0}--- response body ({4} chars) ---{0}{5}{0}--- end response body ---",
            Environment.NewLine,
            (int)response.StatusCode, response.ReasonPhrase,
            FormatHeaders(response.Headers, response.Content?.Headers),
            bodyText.Length,
            bodyText);
    }

    private static string FormatHeaders(HttpHeaders? headers, HttpHeaders? contentHeaders)
    {
        var lines = new List<string>();
        foreach (var pair in AllPairs(headers).Concat(AllPairs(contentHeaders)))
        {
            lines.Add("    " + pair.Key + ": " + string.Join(", ", pair.Value));
        }
        return lines.Count == 0 ? "    (no headers)" : string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<KeyValuePair<string, IEnumerable<string>>> AllPairs(HttpHeaders? headers) =>
        headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>();

    private static string DecodeForLog(byte[] bytes, string? contentType)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return "(empty body)";
        }

        var ct = (contentType ?? string.Empty).ToLowerInvariant();
        var looksTextual = ct.Contains("xml") || ct.Contains("text") ||
                           ct.Contains("json") || ct.Contains("soap") || ct.Length == 0;
        if (!looksTextual)
        {
            return $"({bytes.Length} bytes of {contentType})";
        }

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not decode payload as UTF-8 for logging.", ex);
            return $"({bytes.Length} bytes, not UTF-8 decodable)";
        }
    }

    private async Task<IActionResult> WriteErrorAsync(
        HttpStatusCode statusCode, string message, CancellationToken cancellationToken)
    {
        Response.StatusCode = (int)statusCode;
        Response.ContentType = "text/plain; charset=utf-8";
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(message), cancellationToken).ConfigureAwait(false);
        return new EmptyResult();
    }
}
