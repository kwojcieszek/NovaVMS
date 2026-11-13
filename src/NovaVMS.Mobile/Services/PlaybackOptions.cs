namespace NovaVMS.Mobile.Services;

/// <summary>How a stream is played: buffer, decoder, frame limit and audio.</summary>
public sealed record PlaybackOptions
{
    /// <summary>Network buffer of the player.</summary>
    public int NetworkCachingMs { get; init; }

    /// <summary>
    /// Decodes with the platform decoder (Android MediaCodec, iOS VideoToolbox), which renders straight to the screen
    /// surface. Frame filters are not applied then: they would force every frame to be copied back to memory.
    /// </summary>
    public bool HardwareDecoding { get; init; }

    /// <summary>Displayed frames per second for software decoding; 0 means no limit.</summary>
    public int MaxFrameRate { get; init; }

    /// <summary>False skips audio decoding entirely (a muted player would still decode it).</summary>
    public bool WithAudio { get; init; }

    /// <summary>Cheaper software decoding for small previews: non-reference frames and deblocking skipped, one thread, late frames dropped.</summary>
    public bool LowCostDecoding { get; init; }

    /// <summary>
    /// Proportions of the camera's main stream (e.g. "16:9"), shared with the desktop application. Every stream of the
    /// camera is shown in them, as on the desktop: sub streams and streams with non-square pixels have other picture sizes.
    /// Null uses the picture size of the stream.
    /// </summary>
    public string? DisplayAspectRatio { get; init; }

    public PlaybackOptions(
        int networkCachingMs,
        bool hardwareDecoding = true,
        int maxFrameRate = 0,
        bool withAudio = true,
        bool lowCostDecoding = false,
        string? displayAspectRatio = null)
    {
        NetworkCachingMs = networkCachingMs;
        HardwareDecoding = hardwareDecoding;
        MaxFrameRate = maxFrameRate;
        WithAudio = withAudio;
        LowCostDecoding = lowCostDecoding;
        DisplayAspectRatio = displayAspectRatio;
    }
}
