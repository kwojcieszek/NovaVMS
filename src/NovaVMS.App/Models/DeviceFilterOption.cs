using NovaVMS.App.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Entry of a device filter list; no device means all devices.
/// </summary>
public sealed class DeviceFilterOption
{
    public DeviceFilterOption(Device? device)
    {
        Device = device;
    }

    public Device? Device { get; }

    public string Label => Device?.DisplayName ?? Localizer.Text("Groups_AllDevices");

    public bool Matches(CameraDescriptor camera)
    {
        return Device is null || camera.Device.Id == Device.Id;
    }
}
