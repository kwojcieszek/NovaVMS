using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Parameters for opening a stream in a live view tile. <paramref name="AspectRatio"/> ("width:height")
/// overrides the proportions of the picture; null keeps those of the stream.
/// </summary>
public sealed record TilePlayRequest(VideoSource Source, StreamType StreamType, string Username, string Password, int NetworkCachingMs, string? AspectRatio = null);
