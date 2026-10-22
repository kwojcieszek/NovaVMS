using NovaVMS.App.Models;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

internal static class GroupLayoutCapacity
{
    public static GridLayoutOption? Suggest(AppSettings settings, GridLayoutOption layout, int cameraCount)
    {
        if (!settings.WarnWhenGroupExceedsLayout || cameraCount <= layout.Count) return null;
        return GridLayoutOption.FitFor(cameraCount);
    }
}
