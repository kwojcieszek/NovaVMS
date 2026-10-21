using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NovaVMS.App.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(NovaVMS.App.Tests.HeadlessTestApplication))]

namespace NovaVMS.App.Tests;

public sealed class HeadlessTestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessTestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

[CollectionDefinition("Tile attachment", DisableParallelization = true)]
public sealed class TileAttachmentCollection;

[Collection("Tile attachment")]
public sealed class VideoTileAttachmentTests
{
    [AvaloniaFact]
    public void MeasuredTileCanDetachAndReattachWhileDescendantsAreNotYetAttached()
    {
        var view = new VideoTileView();
        // Exercise the real tile/overlay lifecycle without native video surfaces in the headless platform.
        var host = view.FindControl<Panel>("VideoHost")!;
        host.Children.Clear();
        var window = new Window { Width = 640, Height = 480, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.Bounds.Width > 0);
            for (var index = 0; index < 5; index++)
            {
                window.Content = null;
                Assert.True(host.Bounds.Width > 0); // Cached size survives detaching, as it does on settings-tab switches.
                window.Content = view;
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
