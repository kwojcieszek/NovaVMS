using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Playback limits that let a phone show many live previews at once.
/// Phones have only a few hardware decoders; the first previews use them (zero-copy rendering, no filters).
/// The remaining previews are decoded in software on purpose, cheaply and with a frame limit, instead of
/// silently falling back when the hardware decoders run out. Previews never decode audio.
/// </summary>
public static class PlaybackPolicy
{
    /// <summary>Mobile networks have more jitter than a LAN; shorter buffers make the picture stutter.</summary>
    public const int MinimumNetworkCachingMs = 300;

    /// <summary>Previews that connect at the same time, so a recorder is not hit by every RTSP session at once.</summary>
    public const int ConcurrentPreviewStarts = 3;

    /// <summary>Previews decoded by the platform decoder; phones commonly offer a handful of concurrent instances.</summary>
    public const int HardwareDecodedPreviews = 4;

    /// <summary>Automatic frame limit of software decoded previews for the number of previews shown together.</summary>
    public static int AutomaticPreviewFrameRate(int previewCount)
    {
        return previewCount switch
        {
            <= 4 => 15,
            <= 9 => 12,
            <= 16 => 8,
            _ => 5
        };
    }

    /// <param name="settings">Synchronized settings; a sub stream frame limit set on the desktop wins over the automatic one.</param>
    /// <param name="previewCount">Number of previews shown together.</param>
    /// <param name="index">Position of the preview; the first ones get the hardware decoders.</param>
    public static PlaybackOptions ForPreview(AppSettings settings, int previewCount, int index)
    {
        var cachingMs = Math.Max(settings.NetworkCachingMs, MinimumNetworkCachingMs);

        if (index < HardwareDecodedPreviews)
        {
            return new PlaybackOptions(cachingMs, HardwareDecoding: true, WithAudio: false);
        }

        var frameRate = settings.SubStreamMaxFrameRate > 0
            ? settings.SubStreamMaxFrameRate
            : AutomaticPreviewFrameRate(previewCount);

        return new PlaybackOptions(cachingMs, HardwareDecoding: false, MaxFrameRate: frameRate, WithAudio: false, LowCostDecoding: true);
    }

    /// <summary>A single camera keeps its audio, full quality and the hardware decoder.</summary>
    public static PlaybackOptions ForCamera(AppSettings settings, StreamType streamType)
    {
        return new PlaybackOptions(Math.Max(settings.GetNetworkCachingMs(streamType), MinimumNetworkCachingMs));
    }
}
