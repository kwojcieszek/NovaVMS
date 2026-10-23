using Avalonia.Headless.XUnit;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class PlaybackShuttleTests
{
    [AvaloniaFact]
    public async Task FastPlaybackSeekRetainsTheVisiblePlayerUntilTheNewRecordingIsReady()
    {
        using var vlc = VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var device = new Device { Channels = [new Channel { Number = 1 }] };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device] }), new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var streams = new List<MemoryStream>();
        var factory = new FakeDeviceClientFactory
        {
            ConfigureClient = client => client.OpenRecording = (_, _, _) => VideoSource.FromStream(() =>
            {
                if (Interlocked.Increment(ref requests) == 2)
                {
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                }
                var stream = new MemoryStream(bytes);
                streams.Add(stream);
                return stream;
            })
        };
        var playback = new PlaybackViewModel(vlc, configuration, factory, null!, new RecordingExporter(vlc));
        Task? seek = null;
        try
        {
            playback.FromTime = null; // Search is independent of this playback transition test.
            playback.SetCamera(new CameraReference { DeviceId = device.Id, ChannelNumber = 1 });
            playback.FromTime = TimeSpan.Zero;
            await playback.SeekCommand.ExecuteAsync(3600d);
            var visible = playback.VideoPlayer;
            var activeSide = playback.IsPrimaryPlayerActive;
            Assert.True(playback.IsPlaying);
            playback.SelectedSpeed = PlaybackSpeedOption.All.Single(option => option.Rate == 4);
            seek = playback.SeekCommand.ExecuteAsync(3660d);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(visible, playback.VideoPlayer);
            Assert.Equal(activeSide, playback.IsPrimaryPlayerActive);
            Assert.True(playback.IsPlaying);
            Assert.True(streams[0].CanRead);
            release.Set();
            await seek.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotSame(visible, playback.VideoPlayer);
            Assert.NotEqual(activeSide, playback.IsPrimaryPlayerActive);
            Assert.Equal(3660d, playback.TimelinePosition);
            Assert.True(playback.IsPlaying);
            Assert.False(streams[0].CanRead);
            playback.StopCommand.Execute(null);
            Assert.False(playback.IsPlaying);
        }
        finally
        {
            release.Set();
            if (seek is not null) await seek;
            playback.Dispose();
            await playback.Disposal;
        }
        Assert.Null(playback.VideoPlayer);
        Assert.Null(playback.PrimaryPlayer);
        Assert.Null(playback.SecondaryPlayer);
        Assert.All(streams, stream => Assert.False(stream.CanRead));
    }
}
