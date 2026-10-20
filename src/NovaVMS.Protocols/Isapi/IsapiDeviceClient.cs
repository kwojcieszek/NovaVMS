using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Resources;
using NovaVMS.Protocols.HcNetSdk;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// Client for Hikvision devices based on the documented HTTP API (ISAPI).
/// Live video and recordings are streamed over standard RTSP.
/// </summary>
public sealed class IsapiDeviceClient : IDeviceClient
{
    private readonly Device _device;
    private readonly IsapiHttpTransport _transport;
    private readonly IsapiDeviceService _deviceService;
    private readonly IsapiPtzService _ptz;
    private readonly IsapiRecordingService _recordings;

    public IsapiDeviceClient(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _transport = new IsapiHttpTransport(device);
        _deviceService = new IsapiDeviceService(_transport);
        _ptz = new IsapiPtzService(_transport, device);
        _recordings = new IsapiRecordingService(_transport);
    }

    public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        return _deviceService.GetDeviceInfoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
    {
        return _deviceService.GetChannelsAsync(cancellationToken);
    }

    public Task<byte[]> GetSnapshotAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        var streamId = IsapiStreamId.Track(channelNumber);
        return _transport.GetBytesAsync($"ISAPI/Streaming/channels/{streamId}/picture", cancellationToken);
    }

    public VideoSource GetLiveSource(int channelNumber, StreamType streamType)
    {
        var streamId = IsapiStreamId.Build(channelNumber, streamType);
        var uri = new UriBuilder("rtsp", _device.Host, _device.RtspPort, $"/Streaming/Channels/{streamId}").Uri;
        return string.IsNullOrEmpty(_device.StreamEncryptionKey) ? VideoSource.FromUri(uri)
            : HikvisionRtspVideoStream.CreateSource(uri, _device, playback: false);
    }

    public Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return _recordings.SearchAsync(channelNumber, from, to, cancellationToken);
    }

    public VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end)
    {
        var trackId = IsapiStreamId.Track(channelNumber);
        var builder = new UriBuilder("rtsp", _device.Host, _device.RtspPort, $"/Streaming/tracks/{trackId}")
        {
            Query = $"starttime={IsapiTime.ToCompact(start)}&endtime={IsapiTime.ToCompact(end)}"
        };
        return string.IsNullOrEmpty(_device.StreamEncryptionKey) ? VideoSource.FromUri(builder.Uri)
            : HikvisionRtspVideoStream.CreateSource(builder.Uri, _device, playback: true);
    }

    public Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default)
    {
        return _ptz.MoveAsync(channelNumber, pan, tilt, zoom, cancellationToken);
    }

    public Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        return _ptz.StopAsync(channelNumber, cancellationToken);
    }

    public Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        return _ptz.GetPresetsAsync(channelNumber, cancellationToken);
    }

    public Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return _ptz.GotoPresetAsync(channelNumber, preset.Id, cancellationToken);
    }

    public void Dispose()
    {
        _transport.Dispose();
    }
}
