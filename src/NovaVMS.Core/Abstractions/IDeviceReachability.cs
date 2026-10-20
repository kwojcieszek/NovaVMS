using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Checks whether a device answers on its control port, without logging in.
/// </summary>
public interface IDeviceReachability
{
    Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default);
}
