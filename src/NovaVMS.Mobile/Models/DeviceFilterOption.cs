using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;

namespace NovaVMS.Mobile.Models;

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

    public bool Matches(MobileCamera camera)
    {
        return Device is null || camera.Device.Id == Device.Id;
    }
}
