using System.Xml.Linq;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// Carries ISAPI requests to a device - over HTTP, or tunnelled through the Hikvision SDK connection.
/// Failures are reported as <see cref="NovaVMS.Core.Exceptions.DeviceCommunicationException"/>.
/// </summary>
internal interface IIsapiTransport : IDisposable
{
    Task<XDocument> GetXmlAsync(string path, CancellationToken cancellationToken);

    Task<XDocument> PostXmlAsync(string path, XElement body, CancellationToken cancellationToken);

    Task PutAsync(string path, XElement? body, CancellationToken cancellationToken);

    Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken);
}
