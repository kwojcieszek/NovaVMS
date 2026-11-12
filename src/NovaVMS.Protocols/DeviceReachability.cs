using System.Net.Sockets;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Protocols;

/// <summary>
/// Opens a TCP connection to the port of the device protocol (HTTP for ISAPI and ONVIF, the SDK port for
/// the Hikvision SDK). No credentials are sent, so frequent checks never count as failed logins.
/// </summary>
public sealed class DeviceReachability : IDeviceReachability
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    public Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        return IsPortReachableAsync(device.Host, device.ConnectionPort, cancellationToken);
    }

    public async Task<bool> IsPortReachableAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(ConnectTimeout);

        try
        {
            using var client = new TcpClient();

            await client.ConnectAsync(host.Trim(), port, timeout.Token).ConfigureAwait(false);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
