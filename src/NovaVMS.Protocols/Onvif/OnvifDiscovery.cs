using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;

namespace NovaVMS.Protocols.Onvif;

/// <summary>
/// Discovers ONVIF devices using WS-Discovery (UDP multicast 239.255.255.250:3702).
/// </summary>
public sealed class OnvifDiscovery : IDeviceDiscovery
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 3702;
    private const string ScopePrefix = "onvif://www.onvif.org/";

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var probe = Encoding.UTF8.GetBytes(BuildProbeMessage());
        var target = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);
        await udpClient.SendAsync(probe, target, cancellationToken).ConfigureAwait(false);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var received = await udpClient.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
                var device = ParseProbeMatch(received.Buffer, received.RemoteEndPoint.Address);
                if (device is not null && !results.ContainsKey(device.Address))
                {
                    results[device.Address] = device;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Discovery time elapsed - return the responses collected so far.
        }

        return results.Values.OrderBy(device => device.Address).ToList();
    }

    private static string BuildProbeMessage()
    {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                        xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
                        xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
              <e:Header>
                <w:MessageID>uuid:{Guid.NewGuid()}</w:MessageID>
                <w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
                <w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
              </e:Header>
              <e:Body>
                <d:Probe>
                  <d:Types>dn:NetworkVideoTransmitter</d:Types>
                </d:Probe>
              </e:Body>
            </e:Envelope>
            """;
    }

    private static DiscoveredDevice? ParseProbeMatch(byte[] buffer, IPAddress sender)
    {
        try
        {
            var document = XDocument.Parse(Encoding.UTF8.GetString(buffer));
            var match = document.DescendantsByLocalName("ProbeMatch").FirstOrDefault();
            if (match is null)
            {
                return null;
            }

            var serviceAddresses = match.ChildValue("XAddrs").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var scopes = match.ChildValue("Scopes").Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return new DiscoveredDevice
            {
                Address = sender.ToString(),
                ServiceAddress = serviceAddresses.FirstOrDefault() ?? string.Empty,
                Name = ReadScope(scopes, "name"),
                Hardware = ReadScope(scopes, "hardware")
            };
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string ReadScope(IEnumerable<string> scopes, string key)
    {
        var prefix = $"{ScopePrefix}{key}/";
        var scope = scopes.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (scope is null)
        {
            return string.Empty;
        }

        return Uri.UnescapeDataString(scope[prefix.Length..]);
    }
}
