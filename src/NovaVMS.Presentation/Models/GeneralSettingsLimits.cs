namespace NovaVMS.Presentation.Models;

/// <summary>
/// Ranges of the numeric general settings, the same on the desktop and on the phone, since the settings are synchronized.
/// An empty field keeps the current value.
/// </summary>
public static class GeneralSettingsLimits
{
    public const int MinNetworkCachingMs = 0;
    public const int MaxNetworkCachingMs = 5000;
    public const int MinHiddenTileReleaseSeconds = 0;
    public const int MaxHiddenTileReleaseSeconds = 3600;
    public const int MinFrameRate = 0;
    public const int MaxFrameRate = 60;

    public static int NetworkCaching(decimal? value, int current)
    {
        return (int)Math.Clamp(value ?? current, MinNetworkCachingMs, MaxNetworkCachingMs);
    }

    public static int HiddenTileRelease(decimal? value, int current)
    {
        return (int)Math.Clamp(value ?? current, MinHiddenTileReleaseSeconds, MaxHiddenTileReleaseSeconds);
    }

    public static int FrameRate(decimal? value, int current)
    {
        return (int)Math.Clamp(value ?? current, MinFrameRate, MaxFrameRate);
    }
}
