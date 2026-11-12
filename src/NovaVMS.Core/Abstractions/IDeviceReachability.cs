using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Checks whether a device answers on its control port, without logging in.
/// </summary>
public interface IDeviceReachability
{
    Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default);

    /// <summary>Checks a single TCP port of the host, for example the RTSP or SDK port of another protocol.</summary>
    Task<bool> IsPortReachableAsync(string host, int port, CancellationToken cancellationToken = default);
}
