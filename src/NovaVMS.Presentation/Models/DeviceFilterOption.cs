using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Presentation.Models;

/// <summary>
/// Entry of a device filter list; no device means all devices.
/// </summary>
public sealed class DeviceFilterOption
{
    public Device? Device { get; }

    public string Label => Device?.DisplayName ?? Localizer.Text("Groups_AllDevices");

    public DeviceFilterOption(Device? device)
    {
        Device = device;
    }

    /// <summary>True when the cameras of <paramref name="device"/> pass the filter.</summary>
    public bool Matches(Device device)
    {
        return Device is null || device.Id == Device.Id;
    }
}
