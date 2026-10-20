namespace NovaVMS.Core.Models;

/// <summary>
/// Recording stored on a device. Times are expressed in the device's local time.
/// </summary>
public sealed class RecordingSegment
{
    public int ChannelNumber { get; init; }

    public DateTime Start { get; init; }

    public DateTime End { get; init; }
}
