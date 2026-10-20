namespace NovaVMS.Core.Models;

/// <summary>
/// General application settings.
/// </summary>
public sealed class AppSettings
{
    public GridStreamMode GridStreamMode { get; set; } = GridStreamMode.Auto;

    /// <summary>
    /// Network buffer for the sub stream (tiles in a split). With many cameras a short buffer makes the
    /// player drop late frames and the picture stutters. The name is kept for existing configuration files.
    /// </summary>
    public int NetworkCachingMs { get; set; } = 1000;

    /// <summary>
    /// Network buffer for the main stream (maximized tile, single view, recordings). Its large key frames
    /// arrive in bursts; a short buffer makes the player drop late frames and the picture stutters.
    /// </summary>
    public int MainStreamCachingMs { get; set; } = 2000;

    public int GetNetworkCachingMs(StreamType streamType)
    {
        return streamType == StreamType.Main ? MainStreamCachingMs : NetworkCachingMs;
    }

    public bool AutoCreateDeviceGroups { get; set; } = true;

    public int DefaultLayoutCount { get; set; } = 4;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public bool ShowTileTitles { get; set; } = true;

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
