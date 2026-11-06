using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NovaVMS.App.Views;
using Xunit;

namespace NovaVMS.App.Tests;

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

            for (var index = 0; index < 100; index++)
            {
                window.Content = null;

                Assert.True(host.Bounds.Width > 0); // Cached size survives detaching, as it does on settings-tab switches.

                window.Content = view;

                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            window.Content = null;

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
