namespace NovaVMS.Core.Models;

/// <summary>
/// Identification data read from a device.
/// </summary>
public sealed class DeviceInfo
{
    public string DeviceName { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public string MacAddress { get; init; } = string.Empty;

    public string FirmwareVersion { get; init; } = string.Empty;

    public DeviceType Type { get; init; } = DeviceType.Unknown;
}
