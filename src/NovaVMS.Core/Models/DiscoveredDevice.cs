namespace NovaVMS.Core.Models;

/// <summary>
/// Device found on the local network during discovery.
/// </summary>
public sealed class DiscoveredDevice
{
    public string Address { get; init; } = string.Empty;

    public string ServiceAddress { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Hardware { get; init; } = string.Empty;
}
