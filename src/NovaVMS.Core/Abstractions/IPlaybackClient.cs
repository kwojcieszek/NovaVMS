using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Searches and plays back recordings stored on a device.
/// </summary>
public interface IPlaybackClient
{
    Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end);

    Task<VideoSource> GetPlaybackSourceAsync(int channelNumber, DateTime start, DateTime end, CancellationToken cancellationToken = default)
        => Task.Run(() => GetPlaybackSource(channelNumber, start, end), cancellationToken);
}
