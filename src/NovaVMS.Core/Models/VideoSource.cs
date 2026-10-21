namespace NovaVMS.Core.Models;

/// <summary>
/// Where the player takes video from: a network address (RTSP) or a stream of container data
/// delivered by a device library (e.g. MPEG program stream from the Hikvision SDK).
/// </summary>
public sealed class VideoSource
{
    private readonly Func<bool, Stream>? _openStream;
    private Func<(Uri Uri, IDisposable Lifetime)>? _openUri;

    private VideoSource(Uri? uri, Func<bool, Stream>? openStream)
    {
        Uri = uri;
        _openStream = openStream;
    }

    /// <summary>
    /// Network address, or null for a data stream source.
    /// </summary>
    public Uri? Uri { get; }

    public bool IsStream => _openStream is not null;

    public static VideoSource FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new VideoSource(uri, null);
    }

    /// <summary>Opens an owned network session for each playback attempt.</summary>
    public static VideoSource FromUri(Func<(Uri Uri, IDisposable Lifetime)> openUri)
    {
        ArgumentNullException.ThrowIfNull(openUri);
        return new VideoSource(null, null) { _openUri = openUri };
    }

    public Uri OpenUri(out IDisposable? lifetime)
    {
        if (_openUri is not null)
        {
            var session = _openUri();
            lifetime = session.Lifetime;
            return session.Uri;
        }
        lifetime = null;
        return Uri ?? throw new InvalidOperationException("The video source has no network address.");
    }

    /// <summary>
    /// Creates a source that opens a new data stream for every playback attempt (e.g. after a reconnect).
    /// Disposing the returned stream stops the transfer.
    /// </summary>
    public static VideoSource FromStream(Func<Stream> openStream)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        return new VideoSource(null, _ => openStream());
    }

    /// <summary>Creates a stream with audio configured before the device starts sending data.</summary>
    public static VideoSource FromStream(Func<bool, Stream> openStream)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        return new VideoSource(null, openStream);
    }

    public Stream OpenStream(bool withAudio = true)
    {
        if (_openStream is null)
        {
            throw new InvalidOperationException("The video source is a network address, not a data stream.");
        }

        return _openStream(withAudio);
    }
}
