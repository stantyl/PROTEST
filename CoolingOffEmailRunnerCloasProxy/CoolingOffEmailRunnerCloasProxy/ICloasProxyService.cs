using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Threading.Tasks;

namespace CoolingOffEmailRunnerCloasProxy
{
    [ServiceContract]
    public interface ICloasProxyService
    {
        // Bare, raw-stream pass-through: whatever the caller POSTs is forwarded
        // to CloasProxyOptions.TargetServiceUrl byte-for-byte, and whatever comes
        // back (status code, content type, body) is returned byte-for-byte, so
        // the SOAP envelope CoolingOffEmailRunnerConsole's CloasService builds
        // and parses never has to change - only the URL it points at does.
        [OperationContract]
        [WebInvoke(Method = "*", UriTemplate = "", BodyStyle = WebMessageBodyStyle.Bare)]
        Task<Stream> Process(Stream requestBody);
    }
}
