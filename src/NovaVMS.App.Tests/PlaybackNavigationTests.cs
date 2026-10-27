using Avalonia.Headless.XUnit;
using LibVLCSharp.Shared;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class PlaybackNavigationTests
{
    [AvaloniaFact]
    public async Task LatestSeekWinsWhileAnEarlierSeekWaitsForLiveStreamsToClose()
    {
        using var vlc = Engine();
        var opened = new List<DateTime>();
        var playback = await CreateAsync(vlc, opened);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.RecordingOpening += (_, args) => args.WaitFor(ready.Task);
        try
        {
            var first = playback.SeekCommand.ExecuteAsync(3600d);
            Assert.True(playback.SeekCommand.CanExecute(7200d));
            var last = playback.SeekCommand.ExecuteAsync(7200d);
            Assert.Empty(opened);
            ready.SetResult();
            await Task.WhenAll(first, last);
            Assert.Equal(playback.Day.AddHours(2), Assert.Single(opened));
            Assert.True(playback.IsPlaying);
            Assert.Equal(7200, playback.TimelinePosition);
        }
        finally { playback.Dispose(); await playback.Disposal; }
    }

    [AvaloniaFact]
    public async Task ReturningToNormalSpeedReopensAtTheDisplayedTimeAndPreservesPause()
    {
        using var vlc = Engine();
        var opened = new List<DateTime>();
        var playback = await CreateAsync(vlc, opened);
        try
        {
            playback.SelectedSpeed = PlaybackSpeedOption.All.Single(speed => speed.Rate == -4);
            await playback.SeekCommand.ExecuteAsync(3600d);
            playback.TogglePauseCommand.Execute(null);
            playback.TimelinePosition = 3500;
            playback.NormalSpeedCommand.Execute(null);
            await WaitForAsync(() => opened.Count == 2 && playback.TimelinePosition == 3500 && playback.VideoPlayer!.State == VLCState.Paused);
            Assert.Equal(playback.Day.AddSeconds(3500), opened[^1]);
            Assert.True(playback.IsPaused);
            Assert.Equal("00:58:20", playback.CurrentTimeText);
        }
        finally { playback.Dispose(); await playback.Disposal; }
    }

    [AvaloniaFact]
    public async Task ChangingDayWhileOpeningCannotRestoreThePreviousDay()
    {
        using var vlc = Engine();
        var opened = new List<DateTime>();
        var playback = await CreateAsync(vlc, opened);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.RecordingOpening += (_, args) => args.WaitFor(ready.Task);
        try
        {
            var first = playback.SeekCommand.ExecuteAsync(3600d);
            playback.SelectedDate = DateTime.Today.AddDays(-2);
            ready.SetResult();
            await first;
            Assert.Empty(opened);
            Assert.False(playback.IsPlaying);
            await playback.SeekCommand.ExecuteAsync(7200d);
            Assert.Equal(playback.Day.AddHours(2), Assert.Single(opened));
        }
        finally { playback.Dispose(); await playback.Disposal; }
    }

    private static async Task<PlaybackViewModel> CreateAsync(LibVLC vlc, List<DateTime> opened)
    {
        var device = new Device { Channels = [new Channel { Number = 1 }] };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device] }), new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        var factory = new FakeDeviceClientFactory
        {
            ConfigureClient = client =>
            {
                client.SearchRecordings = (_, from, to, _) => Task.FromResult<IReadOnlyList<RecordingSegment>>(
                    [new RecordingSegment { ChannelNumber = 1, Start = from, End = to }]);
                client.OpenRecording = (_, start, _) =>
                {
                    opened.Add(start);
                    return VideoSource.FromStream(() => new MemoryStream(bytes));
                };
            }
        };
        var playback = new PlaybackViewModel(vlc, configuration, factory, null!, new RecordingExporter(vlc));
        playback.SetCamera(new CameraReference { DeviceId = device.Id, ChannelNumber = 1 });
        await (playback.SearchCommand.ExecutionTask ?? Task.CompletedTask);
        return playback;
    }

    private static LibVLC Engine() => VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
    private static async Task WaitForAsync(Func<bool> ready)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!ready() && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Assert.True(ready());
    }
}
