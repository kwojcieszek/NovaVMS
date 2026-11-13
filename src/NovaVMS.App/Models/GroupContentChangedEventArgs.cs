using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Cameras of a tree group changed (added, removed or reordered).
/// </summary>
public sealed class GroupContentChangedEventArgs : EventArgs
{
    /// <summary>
    /// Key under which the live view remembers the group's split and camera placement.
    /// </summary>
    public string ViewKey { get; }

    /// <summary>
    /// All cameras of the group in tree order, regardless of the search filter.
    /// </summary>
    public IReadOnlyList<CameraReference> Cameras { get; }

    public GroupContentChangedEventArgs(string viewKey, IReadOnlyList<CameraReference> cameras)
    {
        ViewKey = viewKey;
        Cameras = cameras;
    }
}
