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
    public IDeviceClient Create(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

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

    /// <summary>
    /// Releases the Hikvision SDK (streams, logins) when the application closes.
    /// </summary>
    public void Dispose()
    {
        HcNetSdkRuntime.Shutdown();
    }
}
