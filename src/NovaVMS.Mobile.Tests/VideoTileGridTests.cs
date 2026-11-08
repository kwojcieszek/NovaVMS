using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NovaVMS.Mobile.Controls;
using NovaVMS.Mobile.Models;
using Xunit;

namespace NovaVMS.Mobile.Tests;

public sealed class VideoTileGridTests
{
    private static readonly Size PortraitScreen = new(320, 800);

    [AvaloniaFact]
    public void ChosenColumnCountKeepsSixteenByNineTiles()
    {
        var grid = CreateGrid(4, columns: 2, PortraitScreen);

        Assert.Equal(new Rect(160, 0, 160, 90), grid.Children[1].Bounds);
        Assert.Equal(new Rect(0, 90, 160, 90), grid.Children[2].Bounds);
    }

    [AvaloniaFact]
    public void ChosenColumnCountGrowsBeyondTheScreenForScrolling()
    {
        // Inside a scroll viewer the height is unlimited.
        var grid = CreateGrid(12, columns: 1, new Size(320, double.PositiveInfinity));

        Assert.Equal(new Size(320, 12 * 180), grid.DesiredSize);
    }

    [AvaloniaFact]
    public void AutomaticCountFillsTheScreenWithTheLargestPictures()
    {
        // A portrait screen fits four 4:3 pictures largest in one column.
        var grid = CreateGrid(4, PreviewColumnsOption.Automatic, PortraitScreen);

        Assert.Equal(new Rect(0, 600, 320, 200), grid.Children[3].Bounds);
    }

    [Fact]
    public void UnknownColumnCountFallsBackToAutomatic()
    {
        Assert.Equal(PreviewColumnsOption.MaximumColumns, PreviewColumnsOption.Find(PreviewColumnsOption.MaximumColumns).Value);
        Assert.True(PreviewColumnsOption.Find(PreviewColumnsOption.MaximumColumns + 1).IsAutomatic);
    }

    private static VideoTileGrid CreateGrid(int tiles, int columns, Size available)
    {
        var grid = new VideoTileGrid
        {
            Columns = columns
        };

        for (var index = 0; index < tiles; index++)
        {
            grid.Children.Add(new Border());
        }

        grid.Measure(available);
        grid.Arrange(new Rect(grid.DesiredSize));

        return grid;
    }
}
