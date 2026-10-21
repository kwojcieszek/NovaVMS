using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using NovaVMS.App.Models;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class VideoTileLifecycleTests : ITileHost
{
    [AvaloniaFact]
    public async Task ClosingWhileNativeMediaOpensWaitsForTheOpeningAndReleasesBothPlayers()
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new MemoryStream();
        var source = VideoSource.FromStream(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return stream;
        });
        var tile = new VideoTileViewModel(vlc, 0, this);
        try
        {
            tile.SetViewReady(true);
            tile.Play(new CameraReference(), "Test", new TilePlayRequest(source, StreamType.Sub, "", "", 100));
            Dispatcher.UIThread.RunJobs();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            tile.Close();
            tile.Dispose();
            Assert.False(tile.Disposal.IsCompleted);
            Assert.NotNull(tile.PrimaryPlayer);
            release.Set();
            await tile.Disposal.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(tile.PrimaryPlayer);
            Assert.Null(tile.SecondaryPlayer);
            Assert.False(stream.CanRead);
            tile.SetViewReady(true);
            tile.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
        finally { release.Set(); tile.Dispose(); await tile.Disposal; }
    }

    public void SelectTile(VideoTileViewModel tile) { }
    public void ToggleMaximize(VideoTileViewModel tile) { }
    public bool CanDropCameras => false;
    public void DropCameras(VideoTileViewModel tile, IReadOnlyList<CameraReference> cameras) { }
    public void ReportMainAspectRatio(CameraReference camera, string aspectRatio) { }
}
