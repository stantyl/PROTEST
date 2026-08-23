using System.Configuration;

namespace CoolingOffEmailRunnerCloasProxy
{
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
