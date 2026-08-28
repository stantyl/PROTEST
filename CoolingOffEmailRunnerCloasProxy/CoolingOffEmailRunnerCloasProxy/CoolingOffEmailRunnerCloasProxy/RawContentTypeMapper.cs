using System.ServiceModel.Channels;

namespace CoolingOffEmailRunnerCloasProxy
{
    // Forces every incoming request body to be treated as a raw byte stream,
    // whatever its Content-Type. Process(Stream) is a byte-for-byte pass-through,
    // but webHttpBinding otherwise maps text/xml and application/xml to
    // WebContentFormat.Xml and rejects the call with "unrecognized http body
    // format value 'Xml'. The expected format value is 'Raw'". WebHttpBinding
    // exposes no way to plug this in from config, so Web.config uses a
    // customBinding whose webMessageEncoding points webContentTypeMapperType here.
    public class RawContentTypeMapper : WebContentTypeMapper
    {
        public override WebContentFormat GetMessageFormatForContentType(string contentType)
        {
            return WebContentFormat.Raw;
        }
    }
}
