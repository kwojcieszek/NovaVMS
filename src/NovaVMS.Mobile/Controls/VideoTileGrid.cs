using Avalonia;
using Avalonia.Controls;

namespace NovaVMS.Mobile.Controls;

/// <summary>
/// Arranges video tiles. Automatically the column count giving the largest pictures is chosen and the cells fill
/// the whole panel, so every preview is on the screen. With a chosen column count the tiles keep 16:9 proportions
/// and the panel is as tall as its rows; it is then scrolled.
/// </summary>
public sealed class VideoTileGrid : Panel
{
    // Most cameras deliver 4:3 or 16:9 pictures; the automatic column count is chosen for a 4:3 picture.
    private const double AutomaticAspectRatio = 4d / 3d;
    private const double ChosenAspectRatio = 16d / 9d;

    /// <summary>Cameras in one row; 0 chooses the count automatically.</summary>
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<VideoTileGrid, int>(nameof(Columns));

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    static VideoTileGrid()
    {
        AffectsMeasure<VideoTileGrid>(ColumnsProperty);
        AffectsArrange<VideoTileGrid>(ColumnsProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = CalculateLayout(availableSize);

        foreach (var child in Children)
        {
            child.Measure(new Size(layout.CellWidth, layout.CellHeight));
        }

        return new Size(layout.CellWidth * layout.Columns, layout.CellHeight * layout.Rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = CalculateLayout(finalSize);
        var visible = Children.Where(child => child.IsVisible).ToList();

        for (var index = 0; index < visible.Count; index++)
        {
            var row = index / layout.Columns;
            var column = index % layout.Columns;
            var cell = new Rect(column * layout.CellWidth, row * layout.CellHeight, layout.CellWidth, layout.CellHeight);

            visible[index].Arrange(cell);
        }

        return finalSize;
    }

    private static int Rows(int count, int columns)
    {
        return (count + columns - 1) / columns;
    }

    private static int AutomaticColumns(int count, double width, double height)
    {
        var best = 1;
        var bestPicture = 0d;

        for (var columns = 1; columns <= count; columns++)
        {
            var pictureWidth = Math.Min(width / columns, height / Rows(count, columns) * AutomaticAspectRatio);

            if (pictureWidth > bestPicture)
            {
                best = columns;
                bestPicture = pictureWidth;
            }
        }

        return best;
    }

    private TileLayout CalculateLayout(Size size)
    {
        var count = Children.Count(child => child.IsVisible);
        var width = size.Width;
        var height = size.Height;

        if (count == 0 || !double.IsFinite(width) || width <= 0)
        {
            return new TileLayout(1, 0, 0, 0);
        }

        if (Columns > 0)
        {
            var cellWidth = width / Columns;

            return new TileLayout(Columns, Rows(count, Columns), cellWidth, cellWidth / ChosenAspectRatio);
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            return new TileLayout(1, 0, 0, 0);
        }

        var automatic = AutomaticColumns(count, width, height);
        var rows = Rows(count, automatic);

        return new TileLayout(automatic, rows, width / automatic, height / rows);
    }

    private readonly record struct TileLayout(int Columns, int Rows, double CellWidth, double CellHeight);
}
