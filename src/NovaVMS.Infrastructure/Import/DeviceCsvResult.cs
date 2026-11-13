using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Import;

/// <summary>
/// Devices read from a CSV file together with notes about rows that needed attention.
/// </summary>
public sealed record DeviceCsvResult
{
    public IReadOnlyList<Device> Devices { get; init; }

    public IReadOnlyList<string> Warnings { get; init; }

    public IReadOnlyList<CameraGroup> Groups { get; init; } = Array.Empty<CameraGroup>();

    public DeviceCsvResult(IReadOnlyList<Device> devices, IReadOnlyList<string> warnings)
    {
        Devices = devices;
        Warnings = warnings;
    }
}
