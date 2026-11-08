using Avalonia;

namespace NovaVMS.Mobile.Services;

/// <summary>Screen mode of the phone: a camera opened from the live previews is shown full screen in landscape.</summary>
public interface IDeviceDisplay
{
    /// <summary>
    /// Part of the screen covered by the status bar, the notch or Dynamic Island and the home indicator, read
    /// from the platform in Avalonia units; null uses the safe area reported by Avalonia.
    /// </summary>
    Thickness? SafeArea => null;

    /// <summary>Hides the system bars and turns the screen to landscape.</summary>
    void EnterFullScreen();

    /// <summary>Shows the system bars and lets the screen follow the device orientation again.</summary>
    void ExitFullScreen();
}
