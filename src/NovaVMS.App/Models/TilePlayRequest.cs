using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Parameters for opening a stream in a live view tile. <paramref name="AspectRatio"/> ("width:height")
/// overrides the proportions of the picture; null keeps those of the stream.
/// <paramref name="MaxFrameRate"/> limits the displayed frames per second; 0 means no limit.
/// </summary>
public sealed record TilePlayRequest
{
    public VideoSource Source { get; init; }

    public StreamType StreamType { get; init; }

    public string Username { get; init; }

    public string Password { get; init; }

    public int NetworkCachingMs { get; init; }

    public string? AspectRatio { get; init; }

    public int MaxFrameRate { get; init; }

    public TilePlayRequest(
        VideoSource source,
        StreamType streamType,
        string username,
        string password,
        int networkCachingMs,
        string? aspectRatio = null,
        int maxFrameRate = 0)
    {
        Source = source;
        StreamType = streamType;
        Username = username;
        Password = password;
        NetworkCachingMs = networkCachingMs;
        AspectRatio = aspectRatio;
        MaxFrameRate = maxFrameRate;
    }
}
