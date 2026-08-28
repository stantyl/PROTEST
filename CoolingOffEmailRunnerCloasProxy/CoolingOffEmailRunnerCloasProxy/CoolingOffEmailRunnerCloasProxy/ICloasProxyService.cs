using System.IO;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Threading.Tasks;

namespace CoolingOffEmailRunnerCloasProxy
{
    [ServiceContract]
    public interface ICloasProxyService
    {
        [OperationContract]
        [WebInvoke(Method = "*", UriTemplate = "", BodyStyle = WebMessageBodyStyle.Bare)]
        Task<Stream> Process(Stream requestBody);
    }
}
