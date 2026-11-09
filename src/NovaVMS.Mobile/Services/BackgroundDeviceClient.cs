using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Runs every device request on the thread pool. The protocol clients do part of their work synchronously
/// (requests, XML parsing, opening sessions) and continue on the calling thread, which on a phone would
/// make the interface stutter while a camera connects.
/// </summary>
public sealed class BackgroundDeviceClient(IDeviceClient inner) : IDeviceClient
{
    public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => inner.GetDeviceInfoAsync(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => inner.GetChannelsAsync(cancellationToken), cancellationToken);

    public Task<byte[]> GetSnapshotAsync(int channelNumber, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.GetSnapshotAsync(channelNumber, cancellationToken), cancellationToken);

    public VideoSource GetLiveSource(int channelNumber, StreamType streamType)
        => inner.GetLiveSource(channelNumber, streamType);

    public Task<VideoSource> GetLiveSourceAsync(int channelNumber, StreamType streamType, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.GetLiveSourceAsync(channelNumber, streamType, cancellationToken), cancellationToken);

    public Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.PtzMoveAsync(channelNumber, pan, tilt, zoom, cancellationToken), cancellationToken);

    public Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.PtzStopAsync(channelNumber, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.GetPtzPresetsAsync(channelNumber, cancellationToken), cancellationToken);

    public Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.GotoPtzPresetAsync(channelNumber, preset, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.SearchRecordingsAsync(channelNumber, from, to, cancellationToken), cancellationToken);

    public VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end)
        => inner.GetPlaybackSource(channelNumber, start, end);

    public Task<VideoSource> GetPlaybackSourceAsync(int channelNumber, DateTime start, DateTime end, CancellationToken cancellationToken = default)
        => Task.Run(() => inner.GetPlaybackSourceAsync(channelNumber, start, end, cancellationToken), cancellationToken);

    public void Dispose()
    {
        inner.Dispose();
    }
}
