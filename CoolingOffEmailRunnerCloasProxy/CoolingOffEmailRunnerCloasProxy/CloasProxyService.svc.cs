using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using log4net;

namespace CoolingOffEmailRunnerCloasProxy
{
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
            var targetUrl = CloasProxyOptions.TargetServiceUrl;
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                Log.Error("CloasProxy.TargetServiceUrl is not configured in Web.config.");
                return WriteError(HttpStatusCode.InternalServerError,
                    "CLOAS proxy is not configured (missing CloasProxy.TargetServiceUrl).");
            }

            var incomingHeaders = WebOperationContext.Current.IncomingRequest.Headers;
            var soapAction = incomingHeaders["SOAPAction"];
            var contentType = incomingHeaders[HttpRequestHeader.ContentType] ?? "text/xml; charset=utf-8";

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

                        WebOperationContext.Current.OutgoingResponse.StatusCode = response.StatusCode;
                        WebOperationContext.Current.OutgoingResponse.ContentType =
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
                return WriteError(HttpStatusCode.BadGateway, "CLOAS proxy error: " + ex.Message);
            }
        }

        private static Stream WriteError(HttpStatusCode statusCode, string message)
        {
            WebOperationContext.Current.OutgoingResponse.StatusCode = statusCode;
            WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain; charset=utf-8";
            return new MemoryStream(Encoding.UTF8.GetBytes(message));
        }
    }
}
