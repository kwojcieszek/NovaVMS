using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

internal static class SavedViewChanges
{
    public static bool Apply(SavedCameraView view, GroupLiveView layout, int? maximizedTileIndex)
    {
        var previous = view.Layout;
        if (previous.LayoutId == layout.LayoutId && previous.LayoutCount == layout.LayoutCount
            && previous.Cameras.SequenceEqual(layout.Cameras) && previous.EmptySlots.SetEquals(layout.EmptySlots)
            && view.MaximizedTileIndex == maximizedTileIndex) return false;
        view.Layout = layout.Copy();
        view.MaximizedTileIndex = maximizedTileIndex;
        return true;
    }
}
