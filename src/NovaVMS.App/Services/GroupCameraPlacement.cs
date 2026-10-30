using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

internal static class GroupCameraPlacement
{
    public static CameraReference?[] Fill(IReadOnlyList<CameraReference?> current,
        IReadOnlyList<CameraReference> cameras, IReadOnlySet<int> emptySlots)
    {
        // Group order is authoritative, including after reordering an already open group.
        // Previously saved tile positions must not override it.
        var result = new CameraReference?[current.Count];
        using var next = cameras.Distinct().GetEnumerator();
        for (var index = 0; index < result.Length; index++)
        {
            if (emptySlots.Contains(index)) continue;
            if (!next.MoveNext()) break;
            result[index] = next.Current;
        }
        return result;
    }
}
