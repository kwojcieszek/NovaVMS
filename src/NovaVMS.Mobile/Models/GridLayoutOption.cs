using NovaVMS.Presentation.Localization;


namespace NovaVMS.Mobile.Models;

/// <summary>
/// Live view split of the desktop application. The phone only edits the synchronized default;
/// identifiers and camera counts match <c>NovaVMS.App.Models.GridLayoutOption</c>.
/// </summary>
public sealed class GridLayoutOption
{
    public static IReadOnlyList<GridLayoutOption> All { get; } = new List<GridLayoutOption>
    {
        new("standard-1", 1), new("standard-4", 4), new("standard-6", 6), new("standard-8", 8),
        new("standard-9", 9), new("standard-13", 13), new("standard-16", 16), new("standard-25", 25),
        new("standard-32", 32), new("standard-36", 36), new("standard-64", 64),
        new("wide-4", 4, true), new("wide-6", 6, true), new("wide-7", 7, true), new("wide-9", 9, true),
        new("wide-12", 12, true), new("wide-16", 16, true), new("wide-24", 24, true),
        new("wide-36", 36, true), new("wide-48", 48, true), new("wide-2", 2, true)
    }.AsReadOnly();

    public string Id { get; }

    public int Count { get; }

    public bool IsWide { get; }

    public string Label => $"{Count} · {Localizer.Text(IsWide ? "Layout_Wide" : "Layout_Standard")}";

    private GridLayoutOption(string id, int count, bool wide = false)
    {
        Id = id;
        Count = count;
        IsWide = wide;
    }

    public static GridLayoutOption Find(string? id, int fallbackCount = 4)
    {
        return All.FirstOrDefault(option => option.Id == id) ?? Find(fallbackCount);
    }

    // Legacy six-camera configurations used a regular 2×3 grid.
    public static GridLayoutOption Find(int count)
    {
        return count == 6
            ? All.First(option => option.Id == "wide-6")
            : All.FirstOrDefault(option => option.Count == count) ?? All[1];
    }
}
