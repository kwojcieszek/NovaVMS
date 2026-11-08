using Avalonia;
using Avalonia.iOS;
using Foundation;
using NovaVMS.Mobile.Native;
using NovaVMS.Mobile.Services;
using UIKit;

namespace NovaVMS.Mobile.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>
{
    private NSObject? _backgroundObserver;
    private NSObject? _foregroundObserver;
    private NSObject? _windowObserver;
    private bool _launchShown;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaVMS");

        App.Platform = new MobilePlatform(directory, new KeychainProtection(), () => new VlcPlayer(), new IosDeviceDisplay());

        _backgroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillResignActiveNotification, OnWillResignActive);
        _foregroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ => App.Shell?.Resume());
        _windowObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIWindow.DidBecomeKeyNotification, OnWindowDidBecomeKey);

        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    // Until Avalonia draws its first frame the window shows the launch screen picture drawn by UIKit.
    private void OnWindowDidBecomeKey(NSNotification notification)
    {
        if (_launchShown || notification.Object is not UIWindow window)
        {
            return;
        }

        _launchShown = true;

        var background = LaunchOverlay.Background;

        window.BackgroundColor = background;

        if (window.RootViewController?.View is { } view)
        {
            view.BackgroundColor = background;
        }

        if (App.Shell is not { } shell)
        {
            return;
        }

        var overlay = new LaunchOverlay(window.Bounds);

        window.AddSubview(overlay);
        overlay.RemoveWhenShown(shell);
    }

    private static async void OnWillResignActive(NSNotification notification)
    {
        var task = UIApplication.SharedApplication.BeginBackgroundTask("Stop camera", () =>
        {
        });

        try
        {
            if (App.Shell is { } shell)
            {
                await shell.SuspendAsync();
            }
        }
        catch
        {
            // Stop can race native surface teardown.
        }
        finally
        {
            UIApplication.SharedApplication.EndBackgroundTask(task);
        }
    }
}
