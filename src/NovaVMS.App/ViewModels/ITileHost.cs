using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Grid that owns live view tiles and reacts to their user interactions.
/// </summary>
public interface ITileHost
{
    void SelectTile(VideoTileViewModel tile);

    void ToggleMaximize(VideoTileViewModel tile);

    void DropCameras(VideoTileViewModel tile, IReadOnlyList<CameraReference> cameras);
}
