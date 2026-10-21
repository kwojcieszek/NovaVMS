using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

internal static class GroupCameraPlacement
{
    public static CameraReference?[] Fill(IReadOnlyList<CameraReference?> current,
        IReadOnlyList<CameraReference> cameras, IReadOnlySet<int> emptySlots)
    {
        var result = current.ToArray();
        var used = current.OfType<CameraReference>().ToHashSet();
        using var next = cameras.Where(camera => !used.Contains(camera)).Distinct().GetEnumerator();
        for (var index = 0; index < result.Length; index++)
        {
            if (result[index] is not null || emptySlots.Contains(index)) continue;
            if (!next.MoveNext()) break;
            result[index] = next.Current;
        }
        return result;
    }
}
