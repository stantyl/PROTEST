using System.Configuration;

namespace CoolingOffEmailRunnerCloasProxy
{
    // Reads straight from Web.config's <appSettings> on every access (no caching),
    // so swapping CloasProxy.TargetServiceUrl only needs an app pool recycle -
    // no rebuild or redeploy of this project or of CoolingOffEmailRunnerConsole.
    internal static class CloasProxyOptions
    {
        public static string TargetServiceUrl =>
            ConfigurationManager.AppSettings["CloasProxy.TargetServiceUrl"];

        public static int TimeoutSeconds
        {
            get
            {
                var value = ConfigurationManager.AppSettings["CloasProxy.TimeoutSeconds"];
                return int.TryParse(value, out var seconds) ? seconds : 300;
            }
        }
    }
}
