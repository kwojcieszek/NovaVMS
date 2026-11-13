using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Models;

/// <summary>Camera (device channel) resolved from the configuration.</summary>
public sealed record MobileCamera
{
    public Device Device { get; init; }

    public Channel Channel { get; init; }

    public string Title => Channel.DisplayName;

    public string Subtitle => Device.DisplayName;

    public MobileCamera(Device device, Channel channel)
    {
        Device = device;
        Channel = channel;
    }
}
