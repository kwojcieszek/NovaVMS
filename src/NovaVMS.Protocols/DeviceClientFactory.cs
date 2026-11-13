using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Localization;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.HcNetSdk;
using NovaVMS.Protocols.Isapi;
using NovaVMS.Protocols.Onvif;

namespace NovaVMS.Protocols;

/// <summary>
/// Selects the client implementation based on the device protocol.
/// </summary>
public sealed class DeviceClientFactory : IDeviceClientFactory, IDisposable
{
    private readonly DeviceAddressSelector _addresses;
    private readonly bool _useOptionalProtocol;

    public DeviceClientFactory() : this(new DeviceReachability())
    {
    }

    public DeviceClientFactory(IDeviceReachability reachability, bool useOptionalProtocol = false)
    {
        _addresses = new DeviceAddressSelector(reachability);
        _useOptionalProtocol = useOptionalProtocol;
    }

    public bool UsesOptionalProtocol(Device device)
    {
        return _useOptionalProtocol && device.OptionalConnection is not null;
    }

    public Device GetConfiguredConnection(Device device)
    {
        return device.ForProtocol(_useOptionalProtocol);
    }

    public string? GetSelectedHost(Device device)
    {
        return _addresses.GetSelectedHost(GetConfiguredConnection(device));
    }

    public Device GetSelectedConnection(Device device)
    {
        return _addresses.GetSelectedConnection(GetConfiguredConnection(device));
    }

    public async Task<IDeviceClient> CreateAsync(Device device, bool rememberAddress = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var selected = await _addresses.ResolveConnectionAsync(GetConfiguredConnection(device), rememberAddress, cancellationToken).ConfigureAwait(false);
        var client = CreateAt(selected);

        try
        {
            if (UsesOptionalProtocol(device) && selected.Protocol == DeviceProtocol.Onvif && selected.Channels.Count == 0)
            {
                selected.Channels = (await client.GetChannelsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            }

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    public IDeviceClient Create(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return CreateAt(GetSelectedConnection(device));
    }

    public void ReleaseUnusedConnections(IReadOnlyCollection<Device> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        devices = devices.Select(GetConfiguredConnection).ToList();

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
                    throw new NotSupportedException(LibraryText.Format("ProtocolNotSupported", device.Protocol));
                }
        }
    }
}
