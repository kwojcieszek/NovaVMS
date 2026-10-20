using System.Xml.Linq;

namespace NovaVMS.Protocols.Onvif;

/// <summary>
/// XML namespaces of the ONVIF services and of SOAP / WS-Security.
/// </summary>
internal static class OnvifNamespaces
{
    public static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    public static readonly XNamespace Device = "http://www.onvif.org/ver10/device/wsdl";
    public static readonly XNamespace Media = "http://www.onvif.org/ver10/media/wsdl";
    public static readonly XNamespace Ptz = "http://www.onvif.org/ver20/ptz/wsdl";
    public static readonly XNamespace Schema = "http://www.onvif.org/ver10/schema";
    public static readonly XNamespace Security = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    public static readonly XNamespace Utility = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

    public const string PasswordDigestType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";
    public const string Base64EncodingType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
}
