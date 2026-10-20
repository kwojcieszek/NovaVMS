using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Creates the client matching the device protocol.
/// </summary>
public interface IDeviceClientFactory
{
    IDeviceClient Create(Device device);
}
