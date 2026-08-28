using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using log4net;

namespace CoolingOffEmailRunnerCloasProxy
{
    // Runs inside the ASP.NET request pipeline so HttpContext.Current is populated:
    // for a byte-for-byte HTTP proxy the incoming SOAPAction / Content-Type headers
    // have to be read from the real HTTP request. WebOperationContext.Current
    // .IncomingRequest.Headers is not reliably populated for a wildcard
    // (Method = "*") raw-stream operation, which is why the forwarded request was
    // reaching CLOAS without its SOAPAction header.
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
    public class CloasProxyService : ICloasProxyService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(CloasProxyService));

        private static readonly Lazy<HttpClient> LazyHttpClient =
            new Lazy<HttpClient>(() => new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(CloasProxyOptions.TimeoutSeconds)
            });

        public async Task<Stream> Process(Stream requestBody)
        {
            
            var outgoingResponse = WebOperationContext.Current.OutgoingResponse;

            var incoming = SnapshotIncomingHeaders();
            LogIncomingHeaders(incoming);

            var soapAction = incoming.Get("SOAPAction");
            var contentType = incoming.Get("Content-Type");
            if (string.IsNullOrEmpty(contentType))
            {
                contentType = "text/xml; charset=utf-8";
            }

            var targetUrl = CloasProxyOptions.TargetServiceUrl;
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                Log.Error("CloasProxy.TargetServiceUrl is not configured in Web.config.");
                return WriteError(outgoingResponse, HttpStatusCode.InternalServerError,
                    "CLOAS proxy is not configured (missing CloasProxy.TargetServiceUrl).");
            }

        
            byte[] requestBytes = new byte[0];
            if (requestBody != null)
            {
                using (var buffer = new MemoryStream())
                {
                    await requestBody.CopyToAsync(buffer).ConfigureAwait(false);
                    requestBytes = buffer.ToArray();
                }
            }

           
            var requestText = DecodeForLog(requestBytes, contentType);

            var stopwatch = Stopwatch.StartNew();
            Log.InfoFormat("Forwarding CLOAS request to {0} ({1} bytes, Content-Type: {2}, SOAPAction: {3})",
                targetUrl, requestBytes.Length, contentType, soapAction ?? "(none)");

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, targetUrl))
                using (var content = new ByteArrayContent(requestBytes))
                {
                    content.Headers.Remove("Content-Type");
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    if (!string.IsNullOrEmpty(soapAction))
                    {
                        request.Headers.TryAddWithoutValidation("SOAPAction", soapAction);
                    }

                    request.Content = content;

                    LogOutgoingRequest(request, requestText);

                    using (var response = await LazyHttpClient.Value.SendAsync(request).ConfigureAwait(false))
                    {
                        var responseBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        stopwatch.Stop();

                        var responseContentType =
                            response.Content.Headers.ContentType?.ToString() ?? "text/xml; charset=utf-8";

                        outgoingResponse.StatusCode = response.StatusCode;
                        outgoingResponse.ContentType = responseContentType;

                        Log.InfoFormat("CLOAS target {0} responded {1} in {2}ms ({3} bytes)",
                            targetUrl, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, responseBytes.Length);
                        LogResponse(response, DecodeForLog(responseBytes, responseContentType));

                        return new MemoryStream(responseBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log.Error($"Error forwarding CLOAS request to {targetUrl} after {stopwatch.ElapsedMilliseconds}ms", ex);
                return WriteError(outgoingResponse, HttpStatusCode.BadGateway, "CLOAS proxy error: " + ex.Message);
            }
        }

        private sealed class IncomingHeaders
        {
            public string Source = "(none)";
            public bool HttpContextAvailable;
            public readonly WebHeaderCollection Values =
                new WebHeaderCollection();

            public string Get(string name) => Values[name];
        }

        private static IncomingHeaders SnapshotIncomingHeaders()
        {
            var result = new IncomingHeaders();

            var httpRequest = HttpContext.Current?.Request;
            result.HttpContextAvailable = httpRequest != null;
            if (httpRequest != null)
            {
                foreach (var key in httpRequest.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        result.Values[key] = httpRequest.Headers[key];
                    }
                }

                if (result.Values.Count > 0)
                {
                    result.Source = "HttpContext.Request.Headers";
                    return result;
                }
            }

            try
            {
                if (OperationContext.Current != null &&
                    OperationContext.Current.IncomingMessageProperties.TryGetValue(
                        HttpRequestMessageProperty.Name, out var raw) &&
                    raw is HttpRequestMessageProperty httpProperty)
                {
                    foreach (var key in httpProperty.Headers.AllKeys)
                    {
                        if (key != null)
                        {
                            result.Values[key] = httpProperty.Headers[key];
                        }
                    }

                    if (result.Values.Count > 0)
                    {
                        result.Source = "OperationContext HttpRequestMessageProperty";
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read HttpRequestMessageProperty headers.", ex);
            }

            // 3. Last resort: the WebHttp operation context.
            try
            {
                var webHeaders = WebOperationContext.Current?.IncomingRequest.Headers;
                if (webHeaders != null)
                {
                    foreach (var key in webHeaders.AllKeys)
                    {
                        if (key != null)
                        {
                            result.Values[key] = webHeaders[key];
                        }
                    }

                    result.Source = "WebOperationContext.IncomingRequest.Headers";
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read WebOperationContext headers.", ex);
            }

            return result;
        }

        private static void LogIncomingHeaders(IncomingHeaders incoming)
        {
            var dump = string.Join(Environment.NewLine,
                incoming.Values.AllKeys.Select(k => "    " + k + ": " + incoming.Values[k]));
            Log.InfoFormat(
                "Incoming headers (HttpContext available: {0}, read from: {1}):{2}{3}",
                incoming.HttpContextAvailable, incoming.Source, Environment.NewLine,
                string.IsNullOrEmpty(dump) ? "    (no headers found)" : dump);
        }

        private static void LogOutgoingRequest(HttpRequestMessage request, string bodyText)
        {
            Log.InfoFormat(
                "Outgoing request to CLOAS:{0}    {1} {2}{0}{3}{0}--- request body ({4} chars) ---{0}{5}{0}--- end request body ---",
                Environment.NewLine,
                request.Method, request.RequestUri,
                FormatHeaders(request.Headers, request.Content?.Headers),
                bodyText?.Length ?? 0,
                bodyText);
        }

        private static void LogResponse(HttpResponseMessage response, string bodyText)
        {
            Log.InfoFormat(
                "Response from CLOAS:{0}    {1} {2}{0}{3}{0}--- response body ({4} chars) ---{0}{5}{0}--- end response body ---",
                Environment.NewLine,
                (int)response.StatusCode, response.ReasonPhrase,
                FormatHeaders(response.Headers, response.Content?.Headers),
                bodyText?.Length ?? 0,
                bodyText);
        }

        // Flattens request-level and content-level headers into one indented,
        // multi-value-aware block for the log.
        private static string FormatHeaders(
            System.Net.Http.Headers.HttpHeaders headers,
            System.Net.Http.Headers.HttpHeaders contentHeaders)
        {
            var lines = new List<string>();
            foreach (var pair in AllPairs(headers).Concat(AllPairs(contentHeaders)))
            {
                lines.Add("    " + pair.Key + ": " + string.Join(", ", pair.Value));
            }
            return lines.Count == 0 ? "    (no headers)" : string.Join(Environment.NewLine, lines);
        }

        private static IEnumerable<KeyValuePair<string, IEnumerable<string>>> AllPairs(
            System.Net.Http.Headers.HttpHeaders headers)
        {
            return headers == null
                ? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()
                : headers;
        }

        // Best-effort text view of a payload for logging. Only decodes when the
        // Content-Type looks textual (xml / text / json / soap); binary payloads
        // are summarised instead so the log stays readable.
        private static string DecodeForLog(byte[] bytes, string contentType)
        {
            if (bytes == null || bytes.Length == 0)
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

        private static Stream WriteError(OutgoingWebResponseContext outgoingResponse, HttpStatusCode statusCode, string message)
        {
            outgoingResponse.StatusCode = statusCode;
            outgoingResponse.ContentType = "text/plain; charset=utf-8";
            return new MemoryStream(Encoding.UTF8.GetBytes(message));
        }
    }
}
