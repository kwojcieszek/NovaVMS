using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Services;
using Xunit;

namespace NovaVMS.Mobile.Tests;

public sealed class PtzSafetyTests
{
    [Fact]
    public async Task NavigationCancellationStillSendsStopWithUncancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new PtzClient
        {
            OnMove = cancellation.Cancel
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MobileDevices.PulseAsync(client, 3, 40, 0, 0, cancellation.Token));

        Assert.True(client.Stopped);
        Assert.False(client.StopTokenCancelled);
    }

    [Fact]
    public async Task FailedMoveStillAttemptsStop()
    {
        using var client = new PtzClient
        {
            OnMove = () => throw new IOException()
        };

        await Assert.ThrowsAsync<IOException>(() => MobileDevices.PulseAsync(client, 1, 0, 40, 0, TestContext.Current.CancellationToken));

        Assert.True(client.Stopped);
    }

    private sealed class PtzClient : IDeviceClient
    {
        public Action? OnMove { get; init; }

        public bool Stopped { get; private set; }

        public bool StopTokenCancelled { get; private set; }

        public Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default)
        {
            OnMove?.Invoke();

            return Task.CompletedTask;
        }

        public Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default)
        {
            Stopped = true;
            StopTokenCancelled = cancellationToken.IsCancellationRequested;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
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

        public Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end)
        {
            throw new NotSupportedException();
        }
    }
}
