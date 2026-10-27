using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Media;
using NovaVMS.App.Views;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class PlaybackDaySelectionTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task CalendarSelectionPreservesRecordingDatesInTheViewAndDrawsItsTimeline(int daysAgo)
    {
        using var vlc = VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var device = new Device { Channels = [new Channel { Number = 1 }] };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device] }), new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var searches = new List<DateTime>();
        var openings = new List<DateTime>();
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        var factory = new FakeDeviceClientFactory
        {
            ConfigureClient = client =>
            {
                client.SearchRecordings = (_, from, to, _) =>
                {
                    searches.Add(from.Date);
                    return Task.FromResult<IReadOnlyList<RecordingSegment>>([new RecordingSegment
                    {
                        ChannelNumber = 1,
                        Start = DateTime.SpecifyKind(from, DateTimeKind.Unspecified),
                        End = DateTime.SpecifyKind(to, DateTimeKind.Unspecified)
                    }]);
                };
                client.OpenRecording = (_, start, _) =>
                {
                    openings.Add(start);
                    return VideoSource.FromStream(() => new MemoryStream(bytes));
                };
            }
        };
        var workspace = new PlaybackWorkspaceViewModel(vlc, configuration, factory, null!, new RecordingExporter(vlc));
        var playback = workspace.SelectedPlayback;
        Window? window = null;
        try
        {
            var view = new PlaybackView { DataContext = workspace };
            window = new Window { Width = 1600, Height = 1000, Content = view };
            window.Show();
            workspace.SetCamera(new CameraReference { DeviceId = device.Id, ChannelNumber = 1 });
            await (playback.SearchCommand.ExecutionTask ?? Task.CompletedTask);
            var calendar = view.GetLogicalDescendants().OfType<CalendarDatePicker>().First();
            var day = DateTime.Today.AddDays(-daysAgo);
            calendar.SetCurrentValue(CalendarDatePicker.SelectedDateProperty, day);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(day, workspace.SelectedDate);
            await (workspace.SearchCommand.ExecutionTask ?? Task.CompletedTask);
            await WaitForAsync(() => playback.Segments.Count > 0 && !playback.SearchCommand.IsRunning);
            // Realize the view bindings before verifying that displaying recording times
            // has not changed their dates or removed coverage from the timeline.
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(day, searches[^1]);
            var timeline = view.GetLogicalDescendants().OfType<NovaVMS.App.Controls.TimelineBar>().Single();
            Assert.Equal(day, timeline.Day);
            Assert.Same(playback.Segments, timeline.Segments);
            var recording = Assert.Single(playback.Segments);
            Assert.Equal(day, recording.Start);
            Assert.Equal(day.AddDays(1).AddSeconds(-1), recording.End);
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) timeline.Render(context);
            Assert.Contains(drawing.Children.OfType<GeometryDrawing>(), item => Equals(item.Brush, timeline.SegmentBrush));

            await workspace.SeekCommand.ExecuteAsync(12 * 3600d);
            Assert.Equal(day.AddHours(12), Assert.Single(openings));
            Assert.True(playback.IsPlaying);
        }
        finally { window?.Close(); workspace.Dispose(); await workspace.Disposal; }
    }

    [AvaloniaFact]
    public async Task PreviousDayLoadsItsTimelineAndPlaysTheChosenTime()
    {
        using var vlc = VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var device = new Device { Channels = [new Channel { Number = 1 }] };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device] }), new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
        var searches = new List<(DateTime From, DateTime To)>();
        var openings = new List<DateTime>();
        var factory = new FakeDeviceClientFactory
        {
            ConfigureClient = client =>
            {
                client.SearchRecordings = async (_, from, to, token) =>
                {
                    searches.Add((from, to));
                    await Task.Delay(200, token);
                    return [new RecordingSegment { ChannelNumber = 1, Start = from.Date.AddHours(-2), End = from.Date.AddHours(6) },
                        new RecordingSegment { ChannelNumber = 1, Start = from.Date.AddHours(8), End = from.Date.AddHours(12) }];
                };
                client.OpenRecording = (_, start, _) =>
                {
                    openings.Add(start);
                    return VideoSource.FromStream(() => new MemoryStream(bytes));
                };
            }
        };
        var playback = new PlaybackViewModel(vlc, configuration, factory, null!, new RecordingExporter(vlc));
        try
        {
            playback.SetCamera(new CameraReference { DeviceId = device.Id, ChannelNumber = 1 });
            var yesterday = DateTime.Today.AddDays(-1);
            playback.SelectedDate = yesterday;
            await Task.WhenAll(playback.SearchCommand.ExecutionTask ?? Task.CompletedTask);
            await WaitForAsync(() => playback.Segments.Count == 2);
            Assert.All(playback.Segments, segment => Assert.Equal(yesterday, segment.Start.Date));

            await playback.SeekCommand.ExecuteAsync(9.5 * 3600);
            Assert.Equal(yesterday.AddHours(9.5), openings[^1]);
            Assert.True(playback.IsPlaying);
            Assert.Equal(9.5 * 3600, playback.TimelinePosition);
            await Task.Delay(1200);
            Assert.InRange(playback.TimelinePosition, 9.5 * 3600, 9.5 * 3600 + 3);
        }
        finally
        {
            playback.Dispose();
            await playback.Disposal;
        }
    }

    private static async Task WaitForAsync(Func<bool> ready)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!ready() && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Assert.True(ready());
    }
}
