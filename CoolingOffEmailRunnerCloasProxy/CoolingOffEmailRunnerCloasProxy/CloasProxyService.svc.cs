using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceModel.Activation;
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

            var soapAction = ReadIncomingHeader("SOAPAction");
            var contentType = ReadIncomingHeader("Content-Type");
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

        // Reads an incoming HTTP request header. Prefers HttpContext (ASP.NET
        // compatibility mode) since that is the actual request as IIS received it;
        // falls back to the WebHttp operation context if HttpContext is somehow
        // unavailable. Must be called synchronously, before the first await.
        private static string ReadIncomingHeader(string name)
        {
            var request = HttpContext.Current?.Request;
            if (request != null)
            {
                if (Log.IsDebugEnabled && !LoggedHeadersForThisRequest())
                {
                    var dump = string.Join(" | ",
                        request.Headers.AllKeys.Select(k => k + ": " + request.Headers[k]));
                    Log.Debug("Incoming request headers: " + dump);
                }

                return request.Headers[name];
            }

            try
            {
                return WebOperationContext.Current?.IncomingRequest.Headers[name];
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read incoming header '" + name + "' from WebOperationContext.", ex);
                return null;
            }
        }

        // Dump the header list at most once per request (ReadIncomingHeader is
        // called twice). Uses HttpContext.Items as a per-request flag.
        private static bool LoggedHeadersForThisRequest()
        {
            var items = HttpContext.Current?.Items;
            if (items == null)
            {
                return false;
            }

            if (items.Contains("CloasProxy.HeadersLogged"))
            {
                return true;
            }

            items["CloasProxy.HeadersLogged"] = true;
            return false;
        }

        private static Stream WriteError(OutgoingWebResponseContext outgoingResponse, HttpStatusCode statusCode, string message)
        {
            outgoingResponse.StatusCode = statusCode;
            outgoingResponse.ContentType = "text/plain; charset=utf-8";
            return new MemoryStream(Encoding.UTF8.GetBytes(message));
        }
    }
}
