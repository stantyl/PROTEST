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
    [Consumes("text/xml", "application/xml", "application/soap+xml", "text/plain")]
    [Produces("text/xml", "text/plain")]
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
