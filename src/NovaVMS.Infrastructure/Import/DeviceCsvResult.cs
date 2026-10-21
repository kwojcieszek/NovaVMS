using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Import;

/// <summary>
/// Devices read from a CSV file together with notes about rows that needed attention.
/// </summary>
public sealed record DeviceCsvResult(IReadOnlyList<Device> Devices, IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<CameraGroup> Groups { get; init; } = Array.Empty<CameraGroup>();
}
