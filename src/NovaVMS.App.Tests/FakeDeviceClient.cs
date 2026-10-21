using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Tests;

/// <summary>
/// Device client that records PTZ commands and disposal.
/// </summary>
internal sealed class FakeDeviceClient(Device device) : IDeviceClient
{
    public Device Device { get; } = device;

    public List<string> Commands { get; } = new();

    public bool IsDisposed { get; private set; }

    public Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default)
    {
        Commands.Add("move");
        return Task.CompletedTask;
    }

    public Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        Commands.Add("stop");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PtzPreset>>(Array.Empty<PtzPreset>());
    }

    public Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default)
    {
        Commands.Add("preset");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end)
    {
        throw new NotSupportedException();
    }

    public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<byte[]> GetSnapshotAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public VideoSource GetLiveSource(int channelNumber, StreamType streamType)
    {
        throw new NotSupportedException();
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
