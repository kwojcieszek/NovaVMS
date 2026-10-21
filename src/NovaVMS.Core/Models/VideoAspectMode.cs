namespace NovaVMS.Core.Models;

/// <summary>
/// How a camera picture is shaped inside its live view tile.
/// </summary>
public enum VideoAspectMode
{
    /// <summary>The camera's own proportions; free space in the tile stays black.</summary>
    Original,

    /// <summary>The picture is stretched to fill the whole tile.</summary>
    Fit
}
