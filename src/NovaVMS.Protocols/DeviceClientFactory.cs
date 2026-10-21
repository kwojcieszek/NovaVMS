using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.HcNetSdk;
using NovaVMS.Protocols.Isapi;
using NovaVMS.Protocols.Onvif;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols;

/// <summary>
/// Selects the client implementation based on the device protocol.
/// </summary>
public sealed class DeviceClientFactory : IDeviceClientFactory, IDisposable
{
    private readonly DeviceAddressSelector _addresses;

    public DeviceClientFactory() : this(new DeviceReachability()) { }

    public DeviceClientFactory(IDeviceReachability reachability)
    {
        _addresses = new DeviceAddressSelector(reachability);
    }

    public string? GetSelectedHost(Device device) => _addresses.GetSelectedHost(device);

    public Device GetSelectedConnection(Device device) => _addresses.GetSelectedConnection(device);

    public async Task<IDeviceClient> CreateAsync(Device device, bool rememberAddress = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var selected = await _addresses.ResolveConnectionAsync(device, rememberAddress, cancellationToken).ConfigureAwait(false);
        return CreateAt(selected);
    }

    public IDeviceClient Create(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return CreateAt(GetSelectedConnection(device));
    }

    private static IDeviceClient CreateAt(Device device)
    {
        switch (device.Protocol)
        {
            case DeviceProtocol.Isapi:
            {
                return new IsapiDeviceClient(device);
            }
            case DeviceProtocol.Onvif:
            {
                return new OnvifDeviceClient(device);
            }
            case DeviceProtocol.HikvisionSdk:
            {
                return new HcNetSdkDeviceClient(device);
            }
            default:
            {
                throw new NotSupportedException(ProtocolText.Format("ProtocolNotSupported", device.Protocol));
            }
        }
    }

    public void ReleaseUnusedConnections(IReadOnlyCollection<Device> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        HcNetSdkSessionPool.RetainOnly(devices);
        _addresses.RetainOnly(devices);
    }

    /// <summary>
    /// Releases the Hikvision SDK (streams, logins) when the application closes.
    /// </summary>
    public void Dispose()
    {
        HcNetSdkRuntime.Shutdown();
    }
}
