using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Searches and plays back recordings stored on a device.
/// </summary>
public interface IPlaybackClient
{
    Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end);
}
