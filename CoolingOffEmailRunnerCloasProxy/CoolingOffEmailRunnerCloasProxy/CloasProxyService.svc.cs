using System;
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
            // Capture everything that depends on the request/operation context up
            // front: this method awaits a real network call with
            // ConfigureAwait(false), so once execution resumes neither
            // HttpContext.Current nor WebOperationContext.Current is guaranteed to
            // still be available. Reading either after the await would throw
            // NullReferenceException - including inside WriteError, which is why a
            // forwarding failure previously surfaced as the generic "server
            // encountered an error" instead of the real message.
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

            byte[] requestBytes;
            using (var buffer = new MemoryStream())
            {
                await requestBody.CopyToAsync(buffer).ConfigureAwait(false);
                requestBytes = buffer.ToArray();
            }

            var stopwatch = Stopwatch.StartNew();
            Log.InfoFormat("Forwarding CLOAS request to {0} ({1} bytes, SOAPAction: {2})",
                targetUrl, requestBytes.Length, soapAction);

            try
            {
                using (var content = new ByteArrayContent(requestBytes))
                {
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    if (!string.IsNullOrEmpty(soapAction))
                    {
                        content.Headers.TryAddWithoutValidation("SOAPAction", soapAction);
                    }

                    using (var response = await LazyHttpClient.Value.PostAsync(targetUrl, content).ConfigureAwait(false))
                    {
                        var responseBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        stopwatch.Stop();

                        outgoingResponse.StatusCode = response.StatusCode;
                        outgoingResponse.ContentType =
                            response.Content.Headers.ContentType?.ToString() ?? "text/xml; charset=utf-8";

                        Log.InfoFormat("CLOAS target {0} responded {1} in {2}ms ({3} bytes)",
                            targetUrl, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, responseBytes.Length);

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

        // A case-insensitive copy of the incoming HTTP request headers, taken from
        // whichever source actually has them. Must be built synchronously, before
        // the first await, while the request context is still on the thread.
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

            // 1. The real ASP.NET request (ASP.NET compatibility mode). This is
            //    the request exactly as IIS received it and is the reliable path.
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

            // 2. WCF's HttpRequestMessageProperty on the incoming message.
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
            var dump = string.Join(" | ",
                incoming.Values.AllKeys.Select(k => k + ": " + incoming.Values[k]));
            Log.InfoFormat(
                "Incoming headers (HttpContext available: {0}, read from: {1}): {2}",
                incoming.HttpContextAvailable, incoming.Source,
                string.IsNullOrEmpty(dump) ? "(no headers found)" : dump);
        }

        private static Stream WriteError(OutgoingWebResponseContext outgoingResponse, HttpStatusCode statusCode, string message)
        {
            outgoingResponse.StatusCode = statusCode;
            outgoingResponse.ContentType = "text/plain; charset=utf-8";
            return new MemoryStream(Encoding.UTF8.GetBytes(message));
        }
    }
}
