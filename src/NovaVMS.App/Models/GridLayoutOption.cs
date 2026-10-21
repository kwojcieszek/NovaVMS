using NovaVMS.App.Localization;

namespace NovaVMS.App.Models;

public readonly record struct GridCell(int Row, int Column, int RowSpan = 1, int ColumnSpan = 1);

public sealed class GridLayoutOption
{
    private GridLayoutOption(string id, int rows, int columns, bool wide = false, int largeRows = 1, int largeColumns = 1)
    {
        Id = id; Rows = rows; Columns = columns; IsWide = wide;
        var cells = new List<GridCell> { new(0, 0, largeRows, largeColumns) };
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                if (row >= largeRows || column >= largeColumns) cells.Add(new(row, column));
        Cells = cells.AsReadOnly();
    }

    public static IReadOnlyList<GridLayoutOption> All { get; } = new List<GridLayoutOption>
    {
        new("standard-1", 1, 1), new("standard-4", 2, 2),
        new("standard-6", 3, 3, largeRows: 2, largeColumns: 2),
        new("standard-8", 4, 4, largeRows: 3, largeColumns: 3),
        new("standard-9", 3, 3), new("standard-13", 4, 4, largeRows: 2, largeColumns: 2),
        new("standard-16", 4, 4), new("standard-25", 5, 5),
        new("standard-32", 4, 8), new("standard-36", 6, 6), new("standard-64", 8, 8),
        new("wide-4", 3, 4, true, 3, 3), new("wide-6", 2, 3, true),
        new("wide-7", 3, 4, true, 3, 2), new("wide-9", 3, 4, true, 2, 2),
        new("wide-12", 3, 4, true), new("wide-16", 4, 4, true),
        new("wide-24", 4, 6, true), new("wide-36", 4, 9, true), new("wide-48", 6, 8, true),
        new("wide-2", 1, 2, true)
    }.AsReadOnly();

    public string Id { get; }
    public int Rows { get; }
    public int Columns { get; }
    public bool IsWide { get; }
    public IReadOnlyList<GridCell> Cells { get; }
    public int Count => Cells.Count;
    public string Label => $"{Count} · {Localizer.Text(IsWide ? "Layout_Wide" : "Layout_Standard")}";
    public static GridLayoutOption Find(string? id, int fallbackCount = 4)
        => All.FirstOrDefault(option => option.Id == id) ?? Find(fallbackCount);
    // Legacy six-camera configurations used a regular 2×3 grid.
    public static GridLayoutOption Find(int count) => count == 6 ? All.First(option => option.Id == "wide-6")
        : All.FirstOrDefault(option => option.Count == count) ?? All[1];
    public static GridLayoutOption FitFor(int count) => All.Where(option => option.Count >= count)
        .OrderBy(option => option.Count).FirstOrDefault() ?? All.First(option => option.Count == 64);
}
