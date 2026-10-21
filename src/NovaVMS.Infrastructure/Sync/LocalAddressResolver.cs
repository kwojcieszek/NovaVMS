using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NovaVMS.Infrastructure.Sync;

/// <summary>
/// Finds the local IP address this computer uses to reach the database server.
/// </summary>
internal static class LocalAddressResolver
{
    public static async Task<string> ResolveAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host.Trim(), cancellationToken).ConfigureAwait(false);
            foreach (var address in addresses)
            {
                // Connecting a UDP socket sends nothing; it only selects the route and its source address.
                using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(new IPEndPoint(address, port));
                if (socket.LocalEndPoint is IPEndPoint local && !IPAddress.IsLoopback(local.Address))
                {
                    return local.Address.ToString();
                }
            }
        }
        catch (SocketException)
        {
            // Fall back to the interface addresses below.
        }

        return FindInterfaceAddress();
    }

    private static string FindInterfaceAddress()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal)
                .ToList();

            var address = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
            return address?.ToString() ?? string.Empty;
        }
        catch (NetworkInformationException)
        {
            return string.Empty;
        }
    }
}
