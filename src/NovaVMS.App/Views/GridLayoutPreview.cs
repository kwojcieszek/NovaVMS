using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NovaVMS.App.Models;

namespace NovaVMS.App.Views;

public sealed class GridLayoutPreview : Control
{
    public static readonly StyledProperty<GridLayoutOption?> LayoutProperty =
        AvaloniaProperty.Register<GridLayoutPreview, GridLayoutOption?>(nameof(Layout));

    public GridLayoutOption? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    static GridLayoutPreview() => AffectsRender<GridLayoutPreview>(LayoutProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Layout is not { } layout)
        {
            return;
        }

        var pen = new Pen(Brushes.Gray, 1);

        foreach (var cell in layout.Cells)
        {
            context.DrawRectangle(Brushes.DimGray, pen, new Rect(
                cell.Column * Bounds.Width / layout.Columns + 1,
                cell.Row * Bounds.Height / layout.Rows + 1,
                Math.Max(0, cell.ColumnSpan * Bounds.Width / layout.Columns - 2),
                Math.Max(0, cell.RowSpan * Bounds.Height / layout.Rows - 2)));
        }
    }
}
