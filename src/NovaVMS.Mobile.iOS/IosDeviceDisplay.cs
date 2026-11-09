using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using NovaVMS.Mobile.Services;
using UIKit;

namespace NovaVMS.Mobile.iOS;

/// <summary>
/// Full screen landscape mode: the scene is turned to landscape and the status bar is hidden.
/// Leaving it lets the interface follow the device orientation again.
/// </summary>
public sealed class IosDeviceDisplay : IDeviceDisplay
{
    private bool _rotationLocked = true;
    private bool _fullScreen;

    /// <summary>
    /// Orientations the interface may take now; returned to UIKit by <see cref="AppDelegate"/>, so turning the
    /// phone cannot rotate a locked interface. Portrait until the settings are read, as rotation is locked by default.
    /// </summary>
    public UIInterfaceOrientationMask SupportedOrientations { get; private set; } = UIInterfaceOrientationMask.Portrait;

    /// <summary>
    /// Safe area of the application window in points, which are Avalonia units on iOS. The bottom inset of the
    /// home indicator is left out, so the navigation tabs sit at the bottom edge of the screen.
    /// </summary>
    public Thickness? SafeArea
    {
        get
        {
            var windows = Scene()?.Windows ?? [];
            var window = windows.FirstOrDefault(candidate => candidate.IsKeyWindow) ?? windows.FirstOrDefault();

            if (window is null)
            {
                return null;
            }

            var insets = window.SafeAreaInsets;

            return new Thickness(insets.Left, insets.Top, insets.Right, 0);
        }
    }

    public void EnterFullScreen()
    {
        _fullScreen = true;

        Apply(UIInterfaceOrientationMask.Landscape, systemBarVisible: false);
    }

    public void ExitFullScreen()
    {
        _fullScreen = false;

        Apply(WindowedOrientations(), systemBarVisible: true);
    }

    public void SetRotationLocked(bool locked)
    {
        _rotationLocked = locked;

        if (!_fullScreen)
        {
            Apply(WindowedOrientations(), systemBarVisible: true);
        }
    }

    private UIInterfaceOrientationMask WindowedOrientations()
    {
        return _rotationLocked ? UIInterfaceOrientationMask.Portrait : UIInterfaceOrientationMask.AllButUpsideDown;
    }

    private void Apply(UIInterfaceOrientationMask orientations, bool systemBarVisible)
    {
        SupportedOrientations = orientations;

        if (MainTopLevel()?.InsetsManager is { } insets)
        {
            insets.IsSystemBarVisible = systemBarVisible;
        }

        // Turning the interface from code requires the scene geometry API of iOS 16.
        if (!OperatingSystem.IsIOSVersionAtLeast(16))
        {
            return;
        }

        var scene = Scene();

        if (scene is null)
        {
            return;
        }

        scene.KeyWindow?.RootViewController?.SetNeedsUpdateOfSupportedInterfaceOrientations();

        var preferences = new UIWindowSceneGeometryPreferencesIOS(orientations);

        scene.RequestGeometryUpdate(preferences, _ =>
        {
            // The system refuses a rotation the application does not declare; the screen then stays as it is.
        });
    }

    private static UIWindowScene? Scene()
    {
        return UIApplication.SharedApplication.ConnectedScenes
            .ToArray()
            .OfType<UIWindowScene>()
            .FirstOrDefault();
    }

    private static TopLevel? MainTopLevel()
    {
        var view = (Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView;

        return view is null ? null : TopLevel.GetTopLevel(view);
    }
}
