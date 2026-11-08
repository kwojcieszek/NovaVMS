using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using NovaVMS.Mobile.Views;

namespace NovaVMS.Mobile.Android;

[Activity(Label = "NovaVMS", Theme = "@style/NovaTheme", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private MobileShell? _shell;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        AndroidDeviceDisplay.Instance.Activity = this;
        _shell = App.Shell;
    }

    protected override async void OnPause()
    {
        base.OnPause();

        if (_shell is { } shell)
        {
            try
            {
                await shell.SuspendAsync();
            }
            catch
            {
                // The OS may tear down the surface while suspension is in progress.
            }
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        _shell?.Resume();
    }

    protected override async void OnDestroy()
    {
        base.OnDestroy();

        if (_shell is { } shell)
        {
            try
            {
                await shell.DisposeAsync();
            }
            catch
            {
                // Process teardown can destroy native resources first.
            }
        }

        _shell = null;

        if (AndroidDeviceDisplay.Instance.Activity == this)
        {
            AndroidDeviceDisplay.Instance.Activity = null;
        }
    }
}
