namespace NovaVMS.Core.Models;

/// <summary>
/// Where the player takes video from: a network address (RTSP) or a stream of container data
/// delivered by a device library (e.g. MPEG program stream from the Hikvision SDK).
/// </summary>
public sealed class VideoSource
{
    private readonly Func<Stream>? _openStream;

    private VideoSource(Uri? uri, Func<Stream>? openStream)
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

    /// <summary>
    /// Creates a source that opens a new data stream for every playback attempt (e.g. after a reconnect).
    /// Disposing the returned stream stops the transfer.
    /// </summary>
    public static VideoSource FromStream(Func<Stream> openStream)
    {
        ArgumentNullException.ThrowIfNull(openStream);
        return new VideoSource(null, openStream);
    }

    public Stream OpenStream()
    {
        if (_openStream is null)
        {
            throw new InvalidOperationException("The video source is a network address, not a data stream.");
        }

        return _openStream();
    }
}
