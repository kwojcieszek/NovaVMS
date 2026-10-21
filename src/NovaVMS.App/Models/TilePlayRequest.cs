using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Parameters for opening a stream in a live view tile. <paramref name="AspectRatio"/> ("width:height")
/// overrides the proportions of the picture; null keeps those of the stream.
/// <paramref name="MaxFrameRate"/> limits the displayed frames per second; 0 means no limit.
/// </summary>
public sealed record TilePlayRequest(VideoSource Source, StreamType StreamType, string Username, string Password, int NetworkCachingMs, string? AspectRatio = null, int MaxFrameRate = 0);
