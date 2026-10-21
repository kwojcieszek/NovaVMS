using Avalonia;
using Avalonia.Controls;
using NovaVMS.App.Models;
using NovaVMS.App.Views;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class GridLayoutTests
{
    public static IEnumerable<object[]> Layouts => GridLayoutOption.All.Select(layout => new object[] { layout.Id });

    [Theory]
    [MemberData(nameof(Layouts))]
    public void EveryLayoutCoversScreenExactlyOnce(string id)
    {
        var layout = GridLayoutOption.Find(id);
        var occupied = new int[layout.Rows, layout.Columns];
        foreach (var cell in layout.Cells)
            for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                    occupied[row, column]++;
        foreach (var count in occupied) Assert.Equal(1, count);
        Assert.Equal(int.Parse(id.Split('-')[1]), layout.Count);
    }

    [Fact]
    public void EqualCameraCountsKeepDistinctLayoutsAndLegacySixStillWorks()
    {
        Assert.NotSame(GridLayoutOption.Find("standard-9"), GridLayoutOption.Find("wide-9"));
        Assert.Equal("wide-6", GridLayoutOption.Find(6).Id);
        Assert.Equal("standard-9", GridLayoutOption.Find(null, 9).Id);
        Assert.Equal(GridLayoutOption.All.Count, GridLayoutOption.All.Select(layout => layout.Id).Distinct().Count());
    }

    [Fact]
    public void LargeTileAndMaximizationUseCorrectBounds()
    {
        var panel = new VideoLayoutPanel { Layout = GridLayoutOption.Find("standard-6") };
        for (var index = 0; index < 6; index++) panel.Children.Add(new Border());
        panel.Measure(new Size(900, 600));
        panel.Arrange(new Rect(0, 0, 900, 600));
        Assert.Equal(new Rect(0, 0, 600, 400), panel.Children[0].Bounds);
        Assert.Equal(new Rect(600, 0, 300, 200), panel.Children[1].Bounds);
        for (var index = 0; index < 6; index++) panel.Children[index].IsVisible = index == 4;
        panel.IsMaximized = true;
        panel.Measure(new Size(900, 600));
        panel.Arrange(new Rect(0, 0, 900, 600));
        Assert.Equal(new Rect(0, 0, 900, 600), panel.Children[4].Bounds);
        panel.IsMaximized = false;
        foreach (var child in panel.Children) child.IsVisible = true;
        panel.Measure(new Size(900, 600));
        panel.Arrange(new Rect(0, 0, 900, 600));
        Assert.Equal(new Rect(0, 0, 600, 400), panel.Children[0].Bounds);
    }
}
