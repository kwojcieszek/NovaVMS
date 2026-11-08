namespace NovaVMS.Mobile.Services;

/// <summary>Screen mode of the phone: a camera opened from the live previews is shown full screen in landscape.</summary>
public interface IDeviceDisplay
{
    /// <summary>Hides the system bars and turns the screen to landscape.</summary>
    void EnterFullScreen();

    /// <summary>Shows the system bars and lets the screen follow the device orientation again.</summary>
    void ExitFullScreen();
}
