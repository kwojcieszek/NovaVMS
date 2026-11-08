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

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaVMS");

        App.Platform = new MobilePlatform(directory, new KeychainProtection(), () => new VlcPlayer());

        _backgroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillResignActiveNotification, OnWillResignActive);
        _foregroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ => App.Shell?.Resume());

        return base.CustomizeAppBuilder(builder).WithInterFont();
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
