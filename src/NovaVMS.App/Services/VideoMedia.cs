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

    private VideoMedia(Media media, Stream? stream, StreamMediaInput? input)
    {
        Media = media;
        _stream = stream;
        _input = input;
    }

    public Media Media { get; }

    public string? ErrorMessage => (_stream as IVideoStreamStatus)?.ErrorMessage;

    public static VideoMedia Create(LibVLC libVlc, VideoSource source, string username, string password, int networkCachingMs, bool withAudio = true)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsStream)
        {
            var stream = source.OpenStream();
            var input = new StreamMediaInput(stream);
            var streamMedia = new Media(libVlc, input);
            streamMedia.AddOption($":network-caching={networkCachingMs}");
            AddAudioOption(streamMedia, withAudio);
            return new VideoMedia(streamMedia, stream, input);
        }

        // Credentials are passed as options so they never appear in the URL or in logs.
        var media = new Media(libVlc, source.Uri!);
        media.AddOption($":rtsp-user={username}");
        media.AddOption($":rtsp-pwd={password}");
        media.AddOption(":rtsp-tcp");
        // High-resolution I-frames can exceed Live555's 250 KB default and get truncated.
        media.AddOption(":rtsp-frame-buffer-size=4194304");
        media.AddOption($":network-caching={networkCachingMs}");
        AddAudioOption(media, withAudio);
        return new VideoMedia(media, null, null);
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
    /// Ends the data stream. Called before the player is stopped: a player waiting for stream data would
    /// otherwise block until the stream's idle timeout.
    /// </summary>
    public void StopTransfer()
    {
        _stream?.Dispose();
    }

    public void Dispose()
    {
        _stream?.Dispose();
        Media.Dispose();
        _input?.Dispose();
    }
}
