using Avalonia.Headless.XUnit;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class PlaybackBandwidthLimitTests
{
    [AvaloniaFact]
    public async Task SeekRejectedWhileThePreviousRecordingIsOpenRetriesAfterReleasingIt()
    {
        using var vlc = VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var device = new Device { Channels = [new Channel { Number = 1 }] };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device] }), new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        var streams = new List<MemoryStream>();
        var rejected = 0;
        var openings = 0;
        var factory = new FakeDeviceClientFactory
        {
            ConfigureClient = client => client.OpenRecording = (_, _, _) => VideoSource.FromStream(() =>
            {
                // A recorder at its bandwidth limit accepts only one playback session.
                if (streams.Any(stream => stream.CanRead))
                {
                    Interlocked.Increment(ref rejected);
                    throw new IOException("Hikvision SDK error 800.");
                }
                var stream = new MemoryStream(bytes);
                streams.Add(stream);
                return stream;
            })
        };
        var playback = new PlaybackViewModel(vlc, configuration, factory, null!, new RecordingExporter(vlc));
        playback.RecordingOpening += (_, _) => openings++;
        try
        {
            playback.FromTime = null; // Search is independent of this playback test.
            playback.SetCamera(new CameraReference { DeviceId = device.Id, ChannelNumber = 1 });
            playback.FromTime = TimeSpan.Zero;
            await playback.SeekCommand.ExecuteAsync(3600d);
            Assert.True(playback.IsPlaying);

            await playback.SeekCommand.ExecuteAsync(7200d);

            Assert.Equal(1, rejected);
            Assert.Equal(2, openings);
            Assert.True(playback.IsPlaying);
            Assert.Equal(7200d, playback.TimelinePosition);
            Assert.Equal(2, streams.Count);
            Assert.False(streams[0].CanRead);
        }
        finally
        {
            playback.Dispose();
            await playback.Disposal;
        }
    }
}
