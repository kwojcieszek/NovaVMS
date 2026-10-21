using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class RecordingTimelineTests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static RecordingSegment Segment(int start, int end, int channel = 1) =>
        new() { Start = Day.AddSeconds(start), End = Day.AddSeconds(end), ChannelNumber = channel };

    [Fact]
    public void AdjacentOverlappingAndDuplicateFilesFormOneContinuousRecording()
    {
        var merged = RecordingTimeline.Merge([Segment(10, 20), Segment(0, 12), Segment(10, 20), Segment(21, 30)], Day, Day.AddDays(1));
        var segment = Assert.Single(merged);
        Assert.Equal(Day, segment.Start);
        Assert.Equal(Day.AddSeconds(30), segment.End);
    }

    [Fact]
    public void GapsAndChannelsArePreservedAndExportRangeIsClipped()
    {
        var merged = RecordingTimeline.Merge([Segment(0, 10), Segment(15, 30), Segment(0, 30, 2), Segment(50, 40)], Day.AddSeconds(5), Day.AddSeconds(20));
        Assert.Equal(3, merged.Count);
        Assert.All(merged, segment => Assert.InRange(segment.Start, Day.AddSeconds(5), Day.AddSeconds(20)));
        Assert.All(merged, segment => Assert.InRange(segment.End, Day.AddSeconds(5), Day.AddSeconds(20)));
        Assert.Equal(2, merged.Count(segment => segment.ChannelNumber == 1));
    }

    [Theory]
    [InlineData(12, false, 15d)]
    [InlineData(12, true, 9.999)]
    [InlineData(5, false, 5d)]
    [InlineData(5, true, 5d)]
    [InlineData(30, false, null)]
    [InlineData(0, true, null)]
    public void ShuttleSkipsRealGapsInRequestedDirection(double seconds, bool backwards, double? expected)
    {
        var located = RecordingTimeline.Locate([Segment(0, 10), Segment(15, 30)], Day.AddSeconds(seconds), backwards);
        Assert.Equal(expected.HasValue ? Day.AddSeconds(expected.Value) : (DateTime?)null, located);
    }

    [Theory]
    [InlineData("12:34:56", 45296)]
    [InlineData("12:34", 45240)]
    [InlineData("24:00:00", null)]
    [InlineData("12:99:00", null)]
    [InlineData("", null)]
    public void ExportTimeRequiresValidTimeOfDay(string text, int? expectedSeconds)
    {
        Assert.Equal(expectedSeconds, PlaybackViewModel.ParseExportTime(text)?.TotalSeconds);
    }
}
