using System.ServiceModel.Channels;

namespace CoolingOffEmailRunnerCloasProxy
{

    public class RawContentTypeMapper : WebContentTypeMapper
    {
        public override WebContentFormat GetMessageFormatForContentType(string contentType)
        {
            return WebContentFormat.Raw;
        }
    }
}
