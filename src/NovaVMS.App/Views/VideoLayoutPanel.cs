using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NovaVMS.App.Models;

namespace NovaVMS.App.Views;

public sealed class VideoLayoutPanel : Panel
{
    public static readonly StyledProperty<GridLayoutOption?> LayoutProperty =
        AvaloniaProperty.Register<VideoLayoutPanel, GridLayoutOption?>(nameof(Layout));
    public static readonly StyledProperty<bool> IsMaximizedProperty =
        AvaloniaProperty.Register<VideoLayoutPanel, bool>(nameof(IsMaximized));
    static VideoLayoutPanel() => AffectsMeasure<VideoLayoutPanel>(LayoutProperty, IsMaximizedProperty);
    public GridLayoutOption? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public bool IsMaximized { get => GetValue(IsMaximizedProperty); set => SetValue(IsMaximizedProperty, value); }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = Layout ?? GridLayoutOption.Find(4);
        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            if (!child.IsVisible) continue;
            if (IsMaximized) child.Arrange(new Rect(finalSize));
            else if (index < layout.Cells.Count)
            {
                var cell = layout.Cells[index];
                child.Arrange(new Rect(finalSize.Width * cell.Column / layout.Columns,
                    finalSize.Height * cell.Row / layout.Rows,
                    finalSize.Width * cell.ColumnSpan / layout.Columns,
                    finalSize.Height * cell.RowSpan / layout.Rows));
            }
        }
        return finalSize;
    }
}

public sealed class GridLayoutPreview : Control
{
    public static readonly StyledProperty<GridLayoutOption?> LayoutProperty =
        AvaloniaProperty.Register<GridLayoutPreview, GridLayoutOption?>(nameof(Layout));
    static GridLayoutPreview() => AffectsRender<GridLayoutPreview>(LayoutProperty);
    public GridLayoutOption? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Layout is not { } layout) return;
        var pen = new Pen(Brushes.Gray, 1);
        foreach (var cell in layout.Cells)
            context.DrawRectangle(Brushes.DimGray, pen, new Rect(
                cell.Column * Bounds.Width / layout.Columns + 1,
                cell.Row * Bounds.Height / layout.Rows + 1,
                Math.Max(0, cell.ColumnSpan * Bounds.Width / layout.Columns - 2),
                Math.Max(0, cell.RowSpan * Bounds.Height / layout.Rows - 2)));
    }
}
