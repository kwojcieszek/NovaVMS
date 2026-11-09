using Android.App;
using Android.Content.PM;
using Android.Views;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.Android;

/// <summary>Full screen landscape mode of the main activity.</summary>
public sealed class AndroidDeviceDisplay : IDeviceDisplay
{
    public static AndroidDeviceDisplay Instance { get; } = new();

    private Activity? _activity;
    private bool _rotationLocked = true;
    private bool _fullScreen;

    /// <summary>Activity currently shown; set by <see cref="MainActivity"/>, which takes the current rotation lock.</summary>
    public Activity? Activity
    {
        get => _activity;
        set
        {
            _activity = value;

            if (value is not null && !_fullScreen)
            {
                value.RequestedOrientation = WindowedOrientation();
            }
        }
    }

    public void EnterFullScreen()
    {
        _fullScreen = true;

        Run(activity =>
        {
            activity.RequestedOrientation = ScreenOrientation.SensorLandscape;

            SetSystemBarsVisible(activity, false);
        });
    }

    public void ExitFullScreen()
    {
        _fullScreen = false;

        Run(activity =>
        {
            activity.RequestedOrientation = WindowedOrientation();

            SetSystemBarsVisible(activity, true);
        });
    }

    public void SetRotationLocked(bool locked)
    {
        _rotationLocked = locked;

        if (!_fullScreen)
        {
            Run(activity => activity.RequestedOrientation = WindowedOrientation());
        }
    }

    // "User" returns to the orientation chosen by the user even when auto-rotation is off.
    private ScreenOrientation WindowedOrientation()
    {
        return _rotationLocked ? ScreenOrientation.Portrait : ScreenOrientation.User;
    }

    private static void SetSystemBarsVisible(Activity activity, bool visible)
    {
        if (activity.Window is not { } window)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30) && window.InsetsController is { } controller)
        {
            if (visible)
            {
                controller.Show(WindowInsets.Type.SystemBars());
            }
            else
            {
                // The bars come back temporarily with a swipe from the screen edge.
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                controller.Hide(WindowInsets.Type.SystemBars());
            }

            return;
        }

#pragma warning disable CA1422 // Flags of system UI visibility are the only option before Android 11.
        window.DecorView.SystemUiFlags = visible
            ? SystemUiFlags.Visible
            : SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky;
#pragma warning restore CA1422
    }

    private void Run(Action<Activity> action)
    {
        if (Activity is { } activity)
        {
            activity.RunOnUiThread(() => action(activity));
        }
    }
}
