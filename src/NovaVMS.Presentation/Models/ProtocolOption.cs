using NovaVMS.Core.Models;

namespace NovaVMS.Presentation.Models;

/// <summary>
/// Entry of the protocol list in the device form.
/// </summary>
public sealed class ProtocolOption : LocalizedOption<DeviceProtocol>
{
    public static IReadOnlyList<ProtocolOption> All { get; } =
    [
        new(DeviceProtocol.Isapi, "Protocol_Hikvision"),
        new(DeviceProtocol.HikvisionSdk, "Protocol_HikvisionSdk"),
        new(DeviceProtocol.Onvif, "Protocol_Onvif")
    ];

    private ProtocolOption(DeviceProtocol value, string labelKey)
        : base(value, labelKey)
    {
    }

    public static ProtocolOption Find(DeviceProtocol value)
    {
        return Find(All, value);
    }

    /// <summary>
    /// Hikvision devices announce themselves in their ONVIF scopes (name "HIKVISION ...", models "DS-...").
    /// </summary>
    public static ProtocolOption ForDiscovered(DiscoveredDevice device)
    {
        var isHikvision = device.Name.Contains("hikvision", StringComparison.OrdinalIgnoreCase)
            || device.Hardware.StartsWith("DS-", StringComparison.OrdinalIgnoreCase)
            || device.Hardware.StartsWith("iDS-", StringComparison.OrdinalIgnoreCase);

        return Find(isHikvision ? DeviceProtocol.Isapi : DeviceProtocol.Onvif);
    }
}
