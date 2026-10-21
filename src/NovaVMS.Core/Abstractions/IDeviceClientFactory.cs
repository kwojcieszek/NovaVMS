using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Creates the client matching the device protocol.
/// </summary>
public interface IDeviceClientFactory
{
    IDeviceClient Create(Device device);

    /// <summary>Selects a reachable address once for camera use; setup can probe without pinning it.</summary>
    Task<IDeviceClient> CreateAsync(Device device, bool rememberAddress = true, CancellationToken cancellationToken = default)
        => Task.FromResult(Create(device));

    /// <summary>Address pinned for this application session, or null before the first camera use.</summary>
    string? GetSelectedHost(Device device) => null;

    /// <summary>Snapshot of the selected address and its associated protocol port.</summary>
    Device GetSelectedConnection(Device device) => device.ForHost(GetSelectedHost(device) ?? device.Host);

    /// <summary>
    /// Releases long-lived connections (e.g. device logins) of devices that are no longer configured
    /// or whose connection settings changed. Blocking; call it off the UI thread.
    /// </summary>
    void ReleaseUnusedConnections(IReadOnlyCollection<Device> devices)
    {
    }
}
