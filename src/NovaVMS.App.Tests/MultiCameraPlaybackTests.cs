using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.App.Views;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class MultiCameraPlaybackTests
{
    [AvaloniaFact]
    public async Task TheViewCanSwitchBetweenOneAndEightTilesAndCollapseExportWithoutLosingItsRestoreButton()
    {
        await using var scene = await Scene.CreateAsync(8);
        var view = new PlaybackView { DataContext = scene.Workspace };
        var window = new Window { Width = 1600, Height = 1000, Content = view };
        window.Show();
        try
        {
            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Button>(),
                button => ReferenceEquals(button.Command, scene.Workspace.SearchCommand));
            for (var count = 1; count <= 8; count++)
            {
                scene.Workspace.SelectedCameraCount = count;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var tiles = view.GetLogicalDescendants().OfType<PlaybackTileView>().ToArray();
                Assert.Equal(count, tiles.Length);
                Assert.All(tiles, tile => Assert.True(tile.Bounds.Width > 0 && tile.Bounds.Height > 0));
            }
            var host = view.FindControl<ItemsControl>("VideoHost")!;
            var expandedWidth = host.Bounds.Width;
            scene.Workspace.IsExportPanelExpanded = false;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.Bounds.Width > expandedWidth);
            Assert.Contains(view.GetLogicalDescendants().OfType<ToggleButton>(), button => button.IsEffectivelyVisible);
            scene.Workspace.IsExportPanelExpanded = true;
            window.UpdateLayout();
            Assert.Equal(expandedWidth, host.Bounds.Width);
            var export = view.GetLogicalDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, scene.Workspace.ExportCommand));
            var exportPosition = export.TranslatePoint(default, view)!.Value;
            Assert.InRange(exportPosition.X + export.Bounds.Width, 0, view.Bounds.Width);
            Assert.InRange(exportPosition.Y + export.Bounds.Height, 0, view.Bounds.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EightCamerasSeekToTheSamePreviousDayAndSharePauseResumeAndStop()
    {
        await using var scene = await Scene.CreateAsync(8);
        var workspace = scene.Workspace;
        workspace.SelectedDate = DateTime.Today.AddDays(-1);
        await workspace.SearchCommand.ExecuteAsync(null);
        await workspace.SeekCommand.ExecuteAsync(12 * 3600d);
        Assert.Equal(8, workspace.Tiles.Count);
        Assert.Equal(4, workspace.Columns);
        Assert.Equal(2, workspace.Rows);
        Assert.Equal(8, scene.Opened.Count);
        Assert.All(scene.Opened, opened => Assert.Equal(workspace.Day.AddHours(12), opened.Start));
        Assert.All(workspace.Tiles, tile => Assert.True(tile.Playback.IsPlaying, tile.Playback.StatusText));
        Assert.All(workspace.Tiles, tile => Assert.True(tile.Playback.VideoPlayer!.IsPlaying));
        await workspace.TogglePauseCommand.ExecuteAsync(null);
        Assert.True(workspace.IsPaused);
        Assert.All(workspace.Tiles, tile => Assert.Equal(VLCState.Paused, tile.Playback.VideoPlayer!.State));
        var pausedAt = workspace.TimelinePosition;
        await Task.Delay(350);
        Assert.Equal(pausedAt, workspace.TimelinePosition);
        await workspace.TogglePauseCommand.ExecuteAsync(null);
        Assert.False(workspace.IsPaused);
        await WaitForAsync(() => workspace.Tiles.All(tile => tile.Playback.VideoPlayer!.IsPlaying));
        workspace.StopCommand.Execute(null);
        Assert.False(workspace.IsPlaying);
        Assert.All(workspace.Tiles, tile => Assert.False(tile.Playback.IsPlaying));
    }

    [AvaloniaFact]
    public async Task ASlowCameraKeepsReadyCamerasPausedUntilEveryPictureIsReady()
    {
        using var release = new ManualResetEventSlim();
        await using var scene = await Scene.CreateAsync(2, openStream: (channel, bytes) =>
        {
            if (channel == 2 && !release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException();
            return new MemoryStream(bytes);
        });
        var workspace = scene.Workspace;
        var seek = workspace.SeekCommand.ExecuteAsync(3600d);
        try
        {
            await WaitForAsync(() => workspace.Tiles[0].Playback.IsPlaying);
            Assert.Equal(VLCState.Paused, workspace.Tiles[0].Playback.VideoPlayer!.State);
            Assert.False(seek.IsCompleted);
            release.Set();
            await seek;
            Assert.All(workspace.Tiles, tile => Assert.True(tile.Playback.IsPlaying));
            await WaitForAsync(() => workspace.Tiles.All(tile => tile.Playback.VideoPlayer!.IsPlaying));
        }
        finally { release.Set(); await seek; }
    }

    [AvaloniaFact]
    public async Task AGapInOneCameraDoesNotMoveItsRecordingToADifferentTime()
    {
        await using var scene = await Scene.CreateAsync(2, coverage: (channel, from, to) => channel == 2
            ? [new RecordingSegment { ChannelNumber = channel, Start = from.AddHours(2), End = to }]
            : [new RecordingSegment { ChannelNumber = channel, Start = from, End = to }]);
        await scene.Workspace.SeekCommand.ExecuteAsync(3600d);
        Assert.Single(scene.Opened);
        Assert.True(scene.Workspace.Tiles[0].Playback.IsPlaying);
        Assert.False(scene.Workspace.Tiles[1].Playback.IsPlaying);
        Assert.Equal(3600, scene.Workspace.Tiles[1].Playback.TimelinePosition);
        await scene.Workspace.SeekCommand.ExecuteAsync(3 * 3600d);
        Assert.All(scene.Workspace.Tiles, tile => Assert.True(tile.Playback.IsPlaying));
        Assert.Equal(1, scene.Opened.Count(opened => opened.Channel == 2));
        Assert.Equal(scene.Workspace.Day.AddHours(3), scene.Opened.Single(opened => opened.Channel == 2).Start);
    }

    [AvaloniaFact]
    public async Task ChangingDayWhileSourcesAreWaitingCannotResumeStalePictures()
    {
        await using var scene = await Scene.CreateAsync(2);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scene.Workspace.RecordingOpening += (_, args) => args.WaitFor(ready.Task);
        var seek = scene.Workspace.SeekCommand.ExecuteAsync(3600d);
        await WaitForAsync(() => scene.Workspace.Tiles.All(tile => tile.Playback.IsPaused));
        scene.Workspace.SelectedDate = DateTime.Today.AddDays(-2);
        ready.SetResult();
        await seek;
        Assert.Empty(scene.Opened);
        Assert.False(scene.Workspace.IsPlaying);
        Assert.All(scene.Workspace.Tiles, tile => Assert.False(tile.Playback.IsPlaying));
        await scene.Workspace.SeekCommand.ExecuteAsync(7200d);
        Assert.Equal(2, scene.Opened.Count);
        Assert.All(scene.Opened, opened => Assert.Equal(scene.Workspace.Day.AddHours(2), opened.Start));
    }

    [AvaloniaFact]
    public async Task ReducingTheLayoutDisposesRemovedPlayersAndExportUsesCheckedCameras()
    {
        await using var scene = await Scene.CreateAsync(8);
        var removed = scene.Workspace.Tiles.Skip(2).ToArray();
        scene.Workspace.SelectTile(scene.Workspace.Tiles[1]);
        Assert.Same(scene.Workspace.Tiles[1].Playback, scene.Workspace.SelectedPlayback);
        scene.Workspace.SelectedCameraCount = 2;
        foreach (var tile in removed) await tile.Playback.Disposal;
        Assert.Equal(2, scene.Workspace.Tiles.Count);
        Assert.All(removed, tile => Assert.Null(tile.Playback.PrimaryPlayer));
        scene.Workspace.SelectTile(scene.Workspace.Tiles[0]);
        scene.Workspace.ClearCamera(scene.Workspace.Tiles[0]);
        Assert.False(scene.Workspace.SelectedPlayback.HasCamera);
        Assert.True(scene.Workspace.ExportCommand.CanExecute(null));
        scene.Workspace.Tiles[1].IsExportSelected = false;
        Assert.False(scene.Workspace.ExportCommand.CanExecute(null));
        Assert.Empty(scene.Workspace.GetExportTargets());
        scene.Workspace.Tiles[1].IsExportSelected = true;
        Assert.Equal(2, Assert.Single(scene.Workspace.GetExportTargets()).Camera.Channel.Number);
        scene.Workspace.SelectTile(scene.Workspace.Tiles[1]);
        Assert.True(scene.Workspace.SelectedPlayback.HasCamera);
        Assert.True(scene.Workspace.ExportCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ExportWritesOnlyCheckedCamerasIntoNamedRecordingFolders()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaVMS-multi-export-" + Guid.NewGuid().ToString("N"));
        var dialogs = new FolderDialogs(directory);
        await using var scene = await Scene.CreateAsync(3, dialogs: dialogs);
        try
        {
            scene.Workspace.SelectedDate = DateTime.Today.AddDays(-1);
            scene.Workspace.Tiles[1].IsExportSelected = false;
            scene.Workspace.ExportFromTime = TimeSpan.Zero;
            scene.Workspace.ExportToTime = TimeSpan.FromSeconds(2);
            var targets = scene.Workspace.GetExportTargets();
            await scene.Workspace.ExportCommand.ExecuteAsync(null);
            Assert.Equal(1, dialogs.Picks);
            Assert.Equal(2, scene.Opened.Count);
            Assert.DoesNotContain(scene.Opened, opened => opened.Channel == 2);
            Assert.All(scene.Opened, opened => Assert.Equal(scene.Workspace.Day, opened.Start));
            foreach (var target in targets)
            {
                var file = Assert.Single(Directory.GetFiles(Path.Combine(directory, target.Folder, "nagrania"), "*.mp4"));
                Assert.InRange(RecordingExporter.ReadMp4Duration(file).TotalSeconds, 1.8, 3.5);
            }
            Assert.Equal(2, Directory.GetFiles(directory, "*.mp4", SearchOption.AllDirectories).Length);
            Assert.False(scene.Workspace.IsExporting);
            Assert.Equal(100, scene.Workspace.ExportProgress);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("../Garage/Entrance", "GarageEntrance")]
    [InlineData("...", "Camera")]
    [InlineData("CON", "_CON")]
    [InlineData("LPT1.txt", "_LPT1.txt")]
    [InlineData("Kotłownia: garaż", "Kotłownia garaż")]
    public void ExportFolderNamesStayInsideTheirCameraDirectory(string input, string expected)
        => Assert.Equal(expected, RecordingExporter.CameraDirectoryName(input));

    [AvaloniaFact]
    public async Task RapidSeeksKeepOnlyTheLatestRequestedTimeOnEveryCamera()
    {
        await using var scene = await Scene.CreateAsync(2);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scene.Workspace.RecordingOpening += (_, args) => args.WaitFor(ready.Task);
        var first = scene.Workspace.SeekCommand.ExecuteAsync(3600d);
        await WaitForAsync(() => scene.Workspace.Tiles.All(tile => tile.Playback.IsPaused));
        var second = scene.Workspace.SeekCommand.ExecuteAsync(7200d);
        ready.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(7200, scene.Workspace.TimelinePosition);
        Assert.All(scene.Workspace.Tiles, tile => Assert.Equal(7200, tile.Playback.TimelinePosition));
        Assert.All(scene.Opened, opened => Assert.Equal(scene.Workspace.Day.AddHours(2), opened.Start));
    }

    private static async Task WaitForAsync(Func<bool> ready)
    {
        var timeout = Stopwatch.StartNew();
        while (!ready() && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Assert.True(ready());
    }

    private sealed class Scene(LibVLC engine, PlaybackWorkspaceViewModel workspace) : IAsyncDisposable
    {
        public PlaybackWorkspaceViewModel Workspace { get; } = workspace;
        public ConcurrentQueue<(int Channel, DateTime Start)> Opened { get; } = new();

        public static async Task<Scene> CreateAsync(int count,
            Func<int, DateTime, DateTime, IReadOnlyList<RecordingSegment>>? coverage = null,
            Func<int, byte[], Stream>? openStream = null, IDialogService? dialogs = null)
        {
            var engine = VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
            var device = new Device { Channels = Enumerable.Range(1, 8).Select(number => new Channel { Number = number }).ToList() };
            var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device] }), new JsonConfigurationSerializer());
            await configuration.LoadAsync();
            var bytes = NativePlaybackLifecycleTests.CreateVideo(250);
            Scene? scene = null;
            var factory = new FakeDeviceClientFactory
            {
                ConfigureClient = client =>
                {
                    client.SearchRecordings = (channel, from, to, _) => Task.FromResult(coverage?.Invoke(channel, from, to)
                        ?? (IReadOnlyList<RecordingSegment>)[new RecordingSegment { ChannelNumber = channel, Start = from, End = to }]);
                    client.OpenRecording = (channel, start, _) =>
                    {
                        scene!.Opened.Enqueue((channel, start));
                        return VideoSource.FromStream(() => openStream?.Invoke(channel, bytes) ?? new MemoryStream(bytes));
                    };
                }
            };
            scene = new Scene(engine, new PlaybackWorkspaceViewModel(engine, configuration, factory, dialogs!, new RecordingExporter(engine)));
            scene.Workspace.SetCameras(device.Channels.Take(count).Select(channel => CameraReference.For(device, channel)).ToArray());
            await scene.Workspace.SearchCommand.ExecuteAsync(null);
            return scene;
        }

        public async ValueTask DisposeAsync()
        {
            Workspace.Dispose();
            await Workspace.Disposal;
            engine.Dispose();
        }
    }

    private sealed class FolderDialogs(string directory) : IDialogService
    {
        public int Picks { get; private set; }
        public Task<string?> PickFolderAsync(string title) { Picks++; return Task.FromResult<string?>(directory); }
        public Task<string?> PromptTextAsync(string title, string message, string initialValue) => throw new NotSupportedException();
        public Task<bool> ConfirmAsync(string title, string message) => throw new NotSupportedException();
        public Task<bool> LoginAsync(UserSessionService session, string userName) => throw new NotSupportedException();
        public Task<Stream?> OpenJsonFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
        public Task<Stream?> OpenCsvFileAsync(string title) => throw new NotSupportedException();
        public Task<(Stream Content, Stream? Setup)?> OpenIvmsFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveCsvFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
    }
}
