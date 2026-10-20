using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Client communicating with a single device.
/// </summary>
public interface IDeviceClient : IPtzClient, IPlaybackClient, IDisposable
{
    Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default);

    Task<byte[]> GetSnapshotAsync(int channelNumber, CancellationToken cancellationToken = default);

    VideoSource GetLiveSource(int channelNumber, StreamType streamType);
}
