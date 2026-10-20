using System.Xml.Linq;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// XML namespace of documents sent to ISAPI.
/// </summary>
internal static class IsapiNamespace
{
    public static readonly XNamespace Value = "http://www.isapi.org/ver20/XMLSchema";

    public const string Version = "2.0";
}
