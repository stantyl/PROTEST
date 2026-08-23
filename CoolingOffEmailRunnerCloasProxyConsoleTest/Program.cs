using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using log4net;
using log4net.Config;

namespace CoolingOffEmailRunnerCloasProxyConsoleTest
{
    internal static class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

        private static int Main(string[] args)
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            XmlConfigurator.Configure();

            Console.WriteLine("=========================================================");
            Console.WriteLine("CoolingOffEmailRunnerCloasProxy Console Test - .NET 4.5.2");
            Console.WriteLine("Started at: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
            Console.WriteLine("=========================================================");

            CloasProxyTestOptions options;
            try
            {
                options = CloasProxyTestOptions.LoadFromAppSettings();
            }
            catch (Exception ex)
            {
                Log.Error("Invalid App.config settings.", ex);
                Console.WriteLine("FATAL: " + ex.Message);
                return 2;
            }

            Log.InfoFormat("Target proxy URL: {0}", options.ServiceUrl);
            Log.InfoFormat("Test plan IDs ({0}): {1}", options.PlanIds.Count, string.Join(", ", options.PlanIds));
            Console.WriteLine("Target proxy URL : " + options.ServiceUrl);
            Console.WriteLine("Test plan IDs    : " + string.Join(", ", options.PlanIds));
            Console.WriteLine("Timeout (s)      : " + options.TimeoutSeconds);
            Console.WriteLine("---------------------------------------------------------");

            try
            {
                var soapEnvelope = BuildSoapEnvelope(options);

                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) })
                using (var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml"))
                {
                    content.Headers.Add("SOAPAction", options.SoapAction);

                    Log.InfoFormat("Posting {0} bytes to {1} (SOAPAction: {2})",
                        Encoding.UTF8.GetByteCount(soapEnvelope), options.ServiceUrl, options.SoapAction);

                    var stopwatch = Stopwatch.StartNew();
                    var response = await httpClient.PostAsync(options.ServiceUrl, content);
                    var responseBody = await response.Content.ReadAsStringAsync();
                    stopwatch.Stop();

                    Console.WriteLine("HTTP status      : " + (int)response.StatusCode + " " + response.StatusCode);
                    Console.WriteLine("Elapsed          : " + stopwatch.ElapsedMilliseconds + " ms");
                    Console.WriteLine("Response bytes   : " + Encoding.UTF8.GetByteCount(responseBody));
                    Console.WriteLine("---------------------------------------------------------");

                    Log.InfoFormat("Proxy responded {0} in {1}ms ({2} bytes)",
                        (int)response.StatusCode, stopwatch.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(responseBody));

                    if (!response.IsSuccessStatusCode)
                    {
                        Log.ErrorFormat("Proxy call failed. Body: {0}", responseBody);
                        Console.WriteLine("FAILED - non-success HTTP status. Raw response body:");
                        Console.WriteLine(responseBody);
                        return 1;
                    }

                    Console.WriteLine("Raw response body:");
                    Console.WriteLine(responseBody);
                    Console.WriteLine("---------------------------------------------------------");

                    PrintParsedSummary(responseBody, options);

                    Console.WriteLine("---------------------------------------------------------");
                    Console.WriteLine("Result: SUCCESS - proxy reachable and returned a SOAP response.");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error while testing the CloasProxy integration.", ex);
                Console.WriteLine("FATAL ERROR: " + ex.Message);
                Console.WriteLine(ex.ToString());
                return 2;
            }
            finally
            {
                LogManager.Shutdown();
            }
        }

        private static string BuildSoapEnvelope(CloasProxyTestOptions options)
        {
            var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, options.TemplateFilePath);
            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException("SOAP template file not found: " + templatePath, templatePath);
            }

            var template = File.ReadAllText(templatePath);
            var policiesXml = string.Join("\n", options.PlanIds.Select(p => $"            <d6p1:string>{p}</d6p1:string>"));

            return template
                .Replace("{CLOAS_NAMESPACE}", options.CloasNamespace)
                .Replace("{CLOAS_POLICY_API_NAMESPACE}", options.CloasPolicyApiNamespace)
                .Replace("{XSI_NAMESPACE}", options.XsiNamespace)
                .Replace("{METHOD_NAME}", options.MethodName)
                .Replace("{SYSTEM_ID}", options.SystemId)
                .Replace("{USER_ID}", options.UserId)
                .Replace("{SYSTEM_REFERENCE}", options.SystemReference)
                .Replace("{POLICIES}", policiesXml);
        }

        private static void PrintParsedSummary(string responseXml, CloasProxyTestOptions options)
        {
            try
            {
                var doc = XDocument.Parse(responseXml);
                XNamespace cloasNs = options.CloasNamespace;

                var policyResult = doc.Descendants(cloasNs + "PolicyResult").FirstOrDefault()
                    ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "PolicyResult");

                if (policyResult == null)
                {
                    Console.WriteLine("Parsed summary  : could not find a <PolicyResult> element - see raw body above.");
                    return;
                }

                var apiRc = GetLocalValue(policyResult, "ApsRc");
                var apiMsg = GetLocalValue(policyResult, "ApsMsg");
                var apiTime = GetLocalValue(policyResult, "ApsTime");

                Console.WriteLine("Parsed ApiRc     : " + apiRc);
                Console.WriteLine("Parsed ApiMsg    : " + apiMsg);
                Console.WriteLine("Parsed ApiTime   : " + apiTime);

                var planResponses = doc.Descendants().Where(e => e.Name.LocalName == "PlanCoolingOffDetailsResponse").ToList();
                Console.WriteLine("Plan responses   : " + planResponses.Count);

                foreach (var plan in planResponses)
                {
                    var policyNumber = GetLocalValue(plan, "PolicyNumber");
                    var sendEmail = GetLocalValue(plan, "SendCoolingOffEmail");
                    var clientCount = plan.Descendants().Count(e => e.Name.LocalName == "ClientReference");
                    Console.WriteLine($"  - PolicyNumber={policyNumber}, SendCoolingOffEmail={sendEmail}, Clients={clientCount}");
                }

                Log.InfoFormat("Parsed response: ApiRc={0}, ApiMsg={1}, PlanResponses={2}", apiRc, apiMsg, planResponses.Count);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not parse response body as SOAP/XML - see raw body in the log above.", ex);
                Console.WriteLine("Parsed summary  : response was not parseable XML (" + ex.Message + ") - see raw body above.");
            }
        }

        private static string GetLocalValue(XElement parent, string localName)
        {
            var element = parent.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
            return element?.Value;
        }
    }

    internal sealed class CloasProxyTestOptions
    {
        public string ServiceUrl { get; private set; }
        public int TimeoutSeconds { get; private set; }
        public string SystemId { get; private set; }
        public string UserId { get; private set; }
        public string SystemReference { get; private set; }
        public string SoapAction { get; private set; }
        public string CloasNamespace { get; private set; }
        public string CloasPolicyApiNamespace { get; private set; }
        public string XsiNamespace { get; private set; }
        public string MethodName { get; private set; }
        public string TemplateFilePath { get; private set; }
        public List<string> PlanIds { get; private set; }

        public static CloasProxyTestOptions LoadFromAppSettings()
        {
            var settings = ConfigurationManager.AppSettings;

            string Require(string key)
            {
                var value = settings[key];
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"Missing required App.config appSetting '{key}'.");
                }
                return value;
            }

            var planIds = Require("Cloas.TestPlanIds")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (planIds.Count == 0)
            {
                throw new InvalidOperationException("Cloas.TestPlanIds must contain at least one plan ID.");
            }

            var timeoutValue = settings["CloasProxy.TimeoutSeconds"];
            var timeoutSeconds = int.TryParse(timeoutValue, out var parsedTimeout) && parsedTimeout > 0 ? parsedTimeout : 60;

            return new CloasProxyTestOptions
            {
                ServiceUrl = Require("CloasProxy.ServiceUrl"),
                TimeoutSeconds = timeoutSeconds,
                SystemId = Require("Cloas.SystemId"),
                UserId = Require("Cloas.UserId"),
                SystemReference = Require("Cloas.SystemReference"),
                SoapAction = Require("Cloas.SoapAction"),
                CloasNamespace = Require("Cloas.CloasNamespace"),
                CloasPolicyApiNamespace = Require("Cloas.CloasPolicyApiNamespace"),
                XsiNamespace = Require("Cloas.XsiNamespace"),
                MethodName = Require("Cloas.MethodName"),
                TemplateFilePath = Require("Cloas.TemplateFilePath"),
                PlanIds = planIds
            };
        }
    }
}
