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
        Reference = new CameraReference
        {
            DeviceId = device.Id,
            ChannelNumber = channel.Number
        };
    }

    public Device Device { get; }

    public Channel Channel { get; }

    public CameraReference Reference { get; }

    public string DisplayName => Channel.DisplayName;

    public string FullName => $"{Channel.DisplayName} · {Device.DisplayName}";
}
