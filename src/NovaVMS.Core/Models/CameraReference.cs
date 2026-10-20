namespace NovaVMS.Core.Models;

/// <summary>
/// Points to a camera: a channel of a specific device.
/// </summary>
public sealed record CameraReference
{
    public Guid DeviceId { get; init; }

    public int ChannelNumber { get; init; }
}
