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
    /// <summary>Safe area of the application window in points, which are Avalonia units on iOS.</summary>
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

            return new Thickness(insets.Left, insets.Top, insets.Right, insets.Bottom);
        }
    }

    public void EnterFullScreen()
    {
        Apply(UIInterfaceOrientationMask.Landscape, systemBarVisible: false);
    }

    public void ExitFullScreen()
    {
        Apply(UIInterfaceOrientationMask.AllButUpsideDown, systemBarVisible: true);
    }

    private static void Apply(UIInterfaceOrientationMask orientations, bool systemBarVisible)
    {
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
