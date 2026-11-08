using Avalonia;
using Avalonia.Controls;

namespace NovaVMS.Mobile.Controls;

/// <summary>
/// Arranges video tiles so that all of them fill the available area: the column count giving the largest
/// pictures is chosen and the cells are stretched to the whole panel. Every camera of a group plays at once
/// and native video views cannot be clipped by a scroll viewer, so the tiles never extend beyond the screen.
/// </summary>
public sealed class VideoTileGrid : Panel
{
    // Most cameras deliver 4:3 or 16:9 pictures; the column count is chosen for a 4:3 picture.
    private const double AspectRatio = 4d / 3d;

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

            visible[index].Arrange(new Rect(column * layout.CellWidth, row * layout.CellHeight, layout.CellWidth, layout.CellHeight));
        }

        return finalSize;
    }

    private static int Rows(int count, int columns)
    {
        return (count + columns - 1) / columns;
    }

    private TileLayout CalculateLayout(Size size)
    {
        var count = Children.Count(child => child.IsVisible);
        var width = size.Width;
        var height = size.Height;

        if (count == 0 || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return new TileLayout(1, 0, 0, 0);
        }

        var best = 1;
        var bestPicture = 0d;

        for (var columns = 1; columns <= count; columns++)
        {
            var pictureWidth = Math.Min(width / columns, height / Rows(count, columns) * AspectRatio);

            if (pictureWidth > bestPicture)
            {
                best = columns;
                bestPicture = pictureWidth;
            }
        }

        var rows = Rows(count, best);

        return new TileLayout(best, rows, width / best, height / rows);
    }

    private readonly record struct TileLayout(int Columns, int Rows, double CellWidth, double CellHeight);
}
