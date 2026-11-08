using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Models;

/// <summary>Camera (device channel) resolved from the configuration.</summary>
public sealed record MobileCamera(Device Device, Channel Channel)
{
    public string Title => Channel.DisplayName;

    public string Subtitle => Device.DisplayName;
}
