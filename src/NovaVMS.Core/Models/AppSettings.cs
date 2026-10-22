namespace NovaVMS.Core.Models;

/// <summary>
/// General application settings.
/// </summary>
public sealed class AppSettings
{
    public bool AutoCheckForUpdates { get; set; }
    public string UpdateServerUrl { get; set; } = string.Empty;

    internal AppSettings Copy() => (AppSettings)MemberwiseClone();
    public GridStreamMode GridStreamMode { get; set; } = GridStreamMode.Auto;

    /// <summary>Proportions of the picture in live view tiles.</summary>
    public VideoAspectMode VideoAspectMode { get; set; } = VideoAspectMode.Original;

    /// <summary>Minimum video area height in physical display pixels for MAIN in automatic mode.</summary>
    public int MainStreamMinimumHeight { get; set; } = 540;

    public StreamType GetLiveStreamType(double videoHeight, bool singleOrMaximized)
    {
        if (GridStreamMode == GridStreamMode.Main) return StreamType.Main;
        if (GridStreamMode == GridStreamMode.Sub) return StreamType.Sub;
        // The view reports its actual size after native surfaces and layout are ready.
        if (!double.IsFinite(videoHeight) || videoHeight <= 0)
            return singleOrMaximized ? StreamType.Main : StreamType.Sub;
        return videoHeight >= Math.Max(1, MainStreamMinimumHeight) ? StreamType.Main : StreamType.Sub;
    }

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

    /// <summary>Maximum displayed frames per second of the sub stream; 0 means no limit.</summary>
    public int SubStreamMaxFrameRate { get; set; }

    /// <summary>Maximum displayed frames per second of the main stream; 0 means no limit.</summary>
    public int MainStreamMaxFrameRate { get; set; }

    public int GetMaxFrameRate(StreamType streamType)
    {
        return streamType == StreamType.Main ? MainStreamMaxFrameRate : SubStreamMaxFrameRate;
    }

    public bool AutoCreateDeviceGroups { get; set; } = true;

    /// <summary>Ask to enlarge the live grid when a camera group exceeds its capacity.</summary>
    public bool WarnWhenGroupExceedsLayout { get; set; } = true;

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
    /// Folder with the Hikvision SDK libraries (HCNetSDK). Relative paths are based on the application
    /// directory; empty also means its "hcnetsdk" subfolder.
    /// </summary>
    public string HcNetSdkDirectory { get; set; } = "hcnetsdk";
}

