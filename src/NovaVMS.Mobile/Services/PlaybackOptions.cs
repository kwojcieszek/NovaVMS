namespace NovaVMS.Mobile.Services;

/// <summary>How a stream is played: buffer, decoder, frame limit and audio.</summary>
/// <param name="NetworkCachingMs">Network buffer of the player.</param>
/// <param name="HardwareDecoding">
/// Decodes with the platform decoder (Android MediaCodec, iOS VideoToolbox), which renders straight to the screen
/// surface. Frame filters are not applied then: they would force every frame to be copied back to memory.
/// </param>
/// <param name="MaxFrameRate">Displayed frames per second for software decoding; 0 means no limit.</param>
/// <param name="WithAudio">False skips audio decoding entirely (a muted player would still decode it).</param>
/// <param name="LowCostDecoding">Cheaper software decoding for small previews: non-reference frames and deblocking skipped, one thread, late frames dropped.</param>
/// <param name="DisplayAspectRatio">
/// Proportions of the camera's main stream (e.g. "16:9"), shared with the desktop application. Every stream of the
/// camera is shown in them, as on the desktop: sub streams and streams with non-square pixels have other picture sizes.
/// Null uses the picture size of the stream.
/// </param>
public sealed record PlaybackOptions(
    int NetworkCachingMs,
    bool HardwareDecoding = true,
    int MaxFrameRate = 0,
    bool WithAudio = true,
    bool LowCostDecoding = false,
    string? DisplayAspectRatio = null);
