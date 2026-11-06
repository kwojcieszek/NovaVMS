namespace NovaVMS.App.Models;

public readonly record struct GridCell(int Row, int Column, int RowSpan = 1, int ColumnSpan = 1);
