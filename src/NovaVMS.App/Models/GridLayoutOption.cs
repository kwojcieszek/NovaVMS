namespace NovaVMS.App.Models;

/// <summary>
/// Available split of the live view grid.
/// </summary>
public sealed class GridLayoutOption
{
    private GridLayoutOption(int rows, int columns)
    {
        Rows = rows;
        Columns = columns;
    }

    public static IReadOnlyList<GridLayoutOption> All { get; } = new List<GridLayoutOption>
    {
        new(1, 1),
        new(1, 2),
        new(2, 2),
        new(2, 3),
        new(3, 3),
        new(4, 4),
        new(5, 5),
        new(6, 6)
    };

    public int Rows { get; }

    public int Columns { get; }

    public int Count => Rows * Columns;

    public string Label => $"{Count} ({Rows}×{Columns})";

    public static GridLayoutOption Find(int count)
    {
        return All.FirstOrDefault(option => option.Count == count) ?? All[2];
    }

    public static GridLayoutOption FitFor(int cameraCount)
    {
        return All.FirstOrDefault(option => option.Count >= cameraCount) ?? All[^1];
    }
}
