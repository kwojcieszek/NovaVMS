using LibVLCSharp.Shared;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// LibVLC media for a <see cref="VideoSource"/>: an RTSP address with credentials passed as options,
/// or a data stream delivered by a device library.
/// </summary>
public sealed class VideoMedia : IDisposable
{
    private readonly Stream? _stream;
    private readonly StreamMediaInput? _input;
    private IDisposable? _networkSession;

    private VideoMedia(Media media, Stream? stream, StreamMediaInput? input)
    {
        // Apply to the input as well as LibVLC defaults. User snapshots must never
        // insert VLC's picture-in-picture preview or filename into the live output.
        media.AddOption(":no-snapshot-preview");
        media.AddOption(":no-osd");
        media.AddOption(":no-video-title-show");
        Media = media;
        _stream = stream;
        _input = input;
    }

    public Media Media { get; }

    public string? ErrorMessage => (_stream as IVideoStreamStatus)?.ErrorMessage
        ?? (_networkSession as IVideoStreamStatus)?.ErrorMessage;

    public static VideoMedia Create(LibVLC libVlc, VideoSource source, string username, string password, int networkCachingMs,
        bool withAudio = true, int maxFrameRate = 0, bool paceStream = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsStream)
        {
            // The device transfer starts when the stream opens; it must end if the media cannot be created.
            var stream = source.OpenStream(withAudio);
            StreamMediaInput? input = null;
            Media? streamMedia = null;
            try
            {
                input = paceStream ? new RecordingMediaInput(stream) : new StreamMediaInput(stream);
                streamMedia = new Media(libVlc, input);
                streamMedia.AddOption($":network-caching={networkCachingMs}");
                AddAudioOption(streamMedia, withAudio);
                AddFrameRateOption(streamMedia, maxFrameRate);
                return new VideoMedia(streamMedia, stream, input);
            }
            catch
            {
                stream.Dispose();
                streamMedia?.Dispose();
                input?.Dispose();
                throw;
            }
        }

        // Credentials are passed as options so they never appear in the URL or in logs.
        var uri = source.OpenUri(out var networkSession);
        Media? media = null;
        try
        {
            media = new Media(libVlc, uri);
            media.AddOption($":rtsp-user={username}");
            media.AddOption($":rtsp-pwd={password}");
            media.AddOption(":rtsp-tcp");
            // High-resolution I-frames can exceed Live555's 250 KB default and get truncated.
            media.AddOption(":rtsp-frame-buffer-size=4194304");
            media.AddOption($":network-caching={networkCachingMs}");
            AddAudioOption(media, withAudio);
            AddFrameRateOption(media, maxFrameRate);
            return new VideoMedia(media, null, null) { _networkSession = networkSession };
        }
        catch
        {
            networkSession?.Dispose();
            media?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A muted player still decodes audio and keeps an audio output synchronizing the video clock.
    /// With many streams carrying audio (e.g. Hikvision main streams) this makes the video stutter.
    /// </summary>
    private static void AddAudioOption(Media media, bool withAudio)
    {
        if (!withAudio)
        {
            media.AddOption(":no-audio");
        }
    }

    /// <summary>
    /// Drops decoded frames above the limit with LibVLC's "fps" video filter, which keeps the stream's
    /// timing intact. Applies to network and SDK streams alike; 0 leaves the frame rate unchanged.
    /// </summary>
    private static void AddFrameRateOption(Media media, int maxFrameRate)
    {
        if (maxFrameRate > 0)
        {
            media.AddOption(":video-filter=fps");
            media.AddOption($":fps-fps={maxFrameRate}");
        }
    }

    /// <summary>
    /// Ends the data stream. Called before the player is stopped: a player waiting for stream data would
    /// otherwise block until the stream's idle timeout.
    /// </summary>
    public void StopTransfer()
    {
        _stream?.Dispose();
        _networkSession?.Dispose();
    }

    /// <summary>
    /// Ends the transfer, stops the player and releases the media, in this order, even if a step fails.
    /// Blocking: run it off the UI thread.
    /// </summary>
    public static void Stop(MediaPlayer player, VideoMedia? media)
    {
        try
        {
            media?.StopTransfer();
        }
        finally
        {
            try
            {
                player.Stop();
            }
            finally
            {
                media?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        StopTransfer();
        Media.Dispose();
        _input?.Dispose();
    }
}
