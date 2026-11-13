namespace NovaVMS.App.Models;

public readonly record struct GridCell
{
    public int Row { get; init; }

    public int Column { get; init; }

    public int RowSpan { get; init; }

    public int ColumnSpan { get; init; }

    public GridCell(int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Row = row;
        Column = column;
        RowSpan = rowSpan;
        ColumnSpan = columnSpan;
    }
}
