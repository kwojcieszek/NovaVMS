using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.App.Views;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class VideoTilePointerTests
{
    [AvaloniaFact]
    public async Task ClosingSettingsRestoresRealMouseDoubleClicksAfterNativeTeardown()
    {
        await using var scene = await Scene.CreateAsync();
        for (var cycle = 0; cycle < 4; cycle++)
        {
            var previous = Assert.IsType<VideoOverlayWindow>(TopLevel.GetTopLevel(scene.Overlay));
            var settings = new Window();
            var result = ModalDialog.ShowAsync<object?>(settings, scene.Owner);
            Dispatcher.UIThread.RunJobs();
            Assert.False(previous.IsVisible);
            Assert.Null(TopLevel.GetTopLevel(scene.Overlay));
            var suppressedDuringClose = false;
            settings.Closed += (_, _) => suppressedDuringClose = scene.Owner.GetValue(ModalDialog.DepthProperty) > 0;
            settings.Close();
            Assert.True(suppressedDuringClose);
            await result;
            Dispatcher.UIThread.RunJobs();
            var restored = Assert.IsType<VideoOverlayWindow>(TopLevel.GetTopLevel(scene.Overlay));
            Assert.NotSame(previous, restored);
            Assert.True(restored.IsVisible);
            Assert.Same(scene.Owner, restored.Owner);
            for (var pair = 0; pair < 2; pair++)
            {
                for (var press = 0; press < 2; press++)
                {
                    restored.MouseDown(new Point(80, 80), MouseButton.Left);
                    restored.MouseUp(new Point(80, 80), MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.Equal(pair == 0, scene.Live.IsMaximized);
            }
        }
    }

    [AvaloniaFact]
    public async Task TileReattachedUnderSettingsRestoresItsInputWindowAfterClosing()
    {
        await using var scene = await Scene.CreateAsync();
        var settings = new Window();
        var result = ModalDialog.ShowAsync<object?>(settings, scene.Owner);
        Dispatcher.UIThread.RunJobs();
        scene.Owner.Content = null;
        scene.Owner.Content = scene.View;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(TopLevel.GetTopLevel(scene.Overlay));
        settings.Close();
        await result;
        Dispatcher.UIThread.RunJobs();
        var restored = Assert.IsType<VideoOverlayWindow>(TopLevel.GetTopLevel(scene.Overlay));
        for (var press = 0; press < 2; press++)
        {
            restored.MouseDown(new Point(80, 80), MouseButton.Left);
            restored.MouseUp(new Point(80, 80), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(scene.Live.IsMaximized);
    }

    [AvaloniaFact]
    public async Task RapidClickSequenceTogglesOncePerPairRatherThanUndoingMaximize()
    {
        await using var scene = await Scene.CreateAsync();
        for (var count = 1; count <= 6; count++)
        {
            scene.Press(scene.Overlay, new Point(80, 80), (ulong)(1000 + count * 80), count);
            Assert.Equal((count / 2) % 2 == 1, scene.Live.IsMaximized);
        }
    }

    [AvaloniaFact]
    public async Task OverlayMovementAndResetNativeClickCountDoNotLoseDoubleClick()
    {
        await using var scene = await Scene.CreateAsync();
        var screen = scene.Overlay.PointToScreen(new Point(120, 100));
        scene.Press(scene.Overlay, scene.Overlay.PointToClient(screen), 1000, 1);
        scene.Owner.Position = new PixelPoint(scene.Owner.Position.X + 20, scene.Owner.Position.Y + 10);
        Dispatcher.UIThread.RunJobs();
        scene.Press(scene.Overlay, scene.Overlay.PointToClient(screen), 1100, 1);
        Assert.True(scene.Live.IsMaximized);
        Assert.Same(scene.Live.Tiles[0], scene.Live.MaximizedTile);
    }

    [AvaloniaFact]
    public async Task ChildControlCannotSwallowTheOverlayDoubleClick()
    {
        await using var scene = await Scene.CreateAsync();
        var child = new Button { Content = "Overlay" };
        ((Panel)scene.Overlay).Children.Add(child);
        Dispatcher.UIThread.RunJobs();
        scene.Press(child, new Point(10, 10), 1000, 1);
        scene.Press(child, new Point(10, 10), 1100, 2);
        Assert.True(scene.Live.IsMaximized);
    }

    [AvaloniaFact]
    public async Task SeparateLocationsAndChangedTileDoNotReuseAnEarlierClick()
    {
        await using var scene = await Scene.CreateAsync();
        scene.Press(scene.Overlay, new Point(40, 40), 1000, 1);
        scene.Press(scene.Overlay, new Point(160, 140), 1100, 2);
        Assert.False(scene.Live.IsMaximized);
        scene.View.DataContext = scene.Live.Tiles[1];
        scene.Press(scene.Overlay, new Point(160, 140), 1200, 3);
        Assert.False(scene.Live.IsMaximized);
        Assert.Same(scene.Live.Tiles[1], scene.Live.SelectedTile);
        scene.Press(scene.Overlay, new Point(160, 140), 1300, 4);
        Assert.Same(scene.Live.Tiles[1], scene.Live.MaximizedTile);
    }

    [AvaloniaFact]
    public async Task HeaderDoubleClickAlsoIgnoresThirdClick()
    {
        await using var scene = await Scene.CreateAsync();
        var header = scene.View.FindControl<Button>("HeaderButton")!;
        scene.Press(header, new Point(20, 10), 1000, 1);
        scene.Press(header, new Point(20, 10), 1100, 2);
        scene.Press(header, new Point(20, 10), 1200, 3);
        Assert.True(scene.Live.IsMaximized);
        scene.Press(header, new Point(20, 10), 1300, 4);
        Assert.False(scene.Live.IsMaximized);
    }

    private sealed class Scene : IAsyncDisposable
    {
        private readonly LibVLC _vlc;
        private readonly Pointer _pointer = new(1, PointerType.Mouse, true);
        internal LiveViewViewModel Live { get; }
        internal VideoTileView View { get; }
        internal Window Owner { get; }
        internal Control Overlay { get; }

        private Scene(LibVLC vlc, LiveViewViewModel live)
        {
            _vlc = vlc;
            Live = live;
            View = new VideoTileView { DataContext = live.Tiles[0] };
            View.FindControl<Panel>("VideoHost")!.Children.Clear();
            Owner = new Window { Width = 640, Height = 480, Content = View };
            Owner.Show();
            Dispatcher.UIThread.RunJobs();
            Overlay = (Control)Assert.Single(Owner.OwnedWindows).Content!;
        }

        internal static async Task<Scene> CreateAsync()
        {
            var configuration = new ConfigurationService(new MemoryConfigurationStore(new()), new JsonConfigurationSerializer());
            await configuration.LoadAsync();
            var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
            var live = new LiveViewViewModel(vlc, configuration, new FakeDeviceClientFactory(), new StatusBarService(), null!, TestSessions.Create(configuration));
            return new Scene(vlc, live);
        }

        internal void Press(Control source, Point position, ulong timestamp, int nativeClickCount)
        {
            var root = TopLevel.GetTopLevel(source)!;
            var args = new PointerPressedEventArgs(source, _pointer, root, source.TranslatePoint(position, root)!.Value,
                timestamp, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, nativeClickCount);
            source.RaiseEvent(args);
            Dispatcher.UIThread.RunJobs();
        }

        public async ValueTask DisposeAsync()
        {
            Owner.Content = null;
            Owner.Close();
            Live.Dispose();
            await Live.Disposal;
            _pointer.Dispose();
            _vlc.Dispose();
        }
    }
}
