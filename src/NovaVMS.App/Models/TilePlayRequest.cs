using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Parameters for opening a stream in a live view tile.
/// </summary>
public sealed record TilePlayRequest(VideoSource Source, StreamType StreamType, string Username, string Password, int NetworkCachingMs);
