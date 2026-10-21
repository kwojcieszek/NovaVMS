using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Grid that owns live view tiles and reacts to their user interactions.
/// </summary>
public interface ITileHost
{
    void SelectTile(VideoTileViewModel tile);

    void ToggleMaximize(VideoTileViewModel tile);

    /// <summary>
    /// Whether cameras dragged from the tree may be dropped on the tiles.
    /// </summary>
    bool CanDropCameras { get; }

    void DropCameras(VideoTileViewModel tile, IReadOnlyList<CameraReference> cameras);

    /// <summary>
    /// Reports the picture proportions ("width:height") of a camera's main stream.
    /// </summary>
    void ReportMainAspectRatio(CameraReference camera, string aspectRatio);

    void ReportVideoHeight(VideoTileViewModel tile) { }
}
