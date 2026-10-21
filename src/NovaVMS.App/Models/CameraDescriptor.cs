using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Camera resolved to its device and channel, ready for display.
/// </summary>
public sealed class CameraDescriptor
{
    public CameraDescriptor(Device device, Channel channel)
    {
        Device = device;
        Channel = channel;
        Reference = CameraReference.For(device, channel);
    }

    public Device Device { get; }

    public Channel Channel { get; }

    public CameraReference Reference { get; }

    public string DisplayName => Channel.DisplayName;

    public string FullName => $"{Channel.DisplayName} · {Device.DisplayName}";
}
