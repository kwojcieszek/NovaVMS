using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Discovers devices on the local network.
/// </summary>
public interface IDeviceDiscovery
{
    Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
