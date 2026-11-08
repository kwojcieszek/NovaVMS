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

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaVMS");

        App.Platform = new MobilePlatform(directory, new KeychainProtection(), () => new VlcPlayer(), new IosDeviceDisplay());

        _backgroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillResignActiveNotification, OnWillResignActive);
        _foregroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ => App.Shell?.Resume());
        _windowObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIWindow.DidBecomeKeyNotification, OnWindowDidBecomeKey);

        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    // Until Avalonia draws its first frame the window shows its own background, which matches the launch screen.
    private static void OnWindowDidBecomeKey(NSNotification notification)
    {
        if (notification.Object is not UIWindow window)
        {
            return;
        }

        var background = UIColor.FromName("LaunchBackground") ?? UIColor.FromRGB(0x1B, 0x1C, 0x1F);

        window.BackgroundColor = background;

        if (window.RootViewController?.View is { } view)
        {
            view.BackgroundColor = background;
        }
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
