namespace NovaVMS.Core.Models;

/// <summary>
/// General application settings.
/// </summary>
public sealed class AppSettings
{
    internal AppSettings Copy() => (AppSettings)MemberwiseClone();
    public GridStreamMode GridStreamMode { get; set; } = GridStreamMode.Auto;

    /// <summary>
    /// Network buffer for the sub stream (tiles in a split). Increase on unstable networks.
    /// The name is kept for existing configuration files.
    /// </summary>
    public int NetworkCachingMs { get; set; } = 150;

    /// <summary>
    /// Network buffer for the main stream (maximized tile, single view, recordings).
    /// Allows more headroom for large key frames; increase on unstable networks.
    /// </summary>
    public int MainStreamCachingMs { get; set; } = 300;

    public int GetNetworkCachingMs(StreamType streamType)
    {
        return streamType == StreamType.Main ? MainStreamCachingMs : NetworkCachingMs;
    }

    public bool AutoCreateDeviceGroups { get; set; } = true;

    public string? DefaultLayoutId { get; set; }
    public int DefaultLayoutCount { get; set; } = 4;

    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    public bool ShowTileTitles { get; set; } = true;

    /// <summary>
    /// How long (seconds) a hidden tile keeps its stream, e.g. behind a maximized tile or on another page.
    /// Restoring the view within this time is instant; after it the connection is released. 0 releases it at once.
    /// </summary>
    public int HiddenTileReleaseSeconds { get; set; } = 30;

    public bool ShowCameraSearch { get; set; } = true;

    /// <summary>
    /// Interface language code ("en", "pl"); empty means the system language.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Folder with the Hikvision SDK libraries (HCNetSDK); empty means the "hcnetsdk" folder next to the application.
    /// </summary>
    public string HcNetSdkDirectory { get; set; } = string.Empty;
}

