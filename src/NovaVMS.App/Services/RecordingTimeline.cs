using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>Device file boundaries are invisible; actual holes in recorded coverage remain visible.</summary>
internal static class RecordingTimeline
{
    public static IReadOnlyList<RecordingSegment> Merge(IEnumerable<RecordingSegment> segments, DateTime from, DateTime to)
    {
        var result = new List<RecordingSegment>();
        foreach (var group in segments.Where(s => s.Start < s.End && s.Start < to && s.End > from).GroupBy(s => s.ChannelNumber))
        {
            RecordingSegment? current = null;
            foreach (var item in group.OrderBy(s => s.Start))
            {
                var start = item.Start < from ? from : item.Start;
                var end = item.End > to ? to : item.End;
                if (current is not null && start <= current.End.AddSeconds(1))
                    current = new RecordingSegment { ChannelNumber = group.Key, Start = current.Start, End = end > current.End ? end : current.End };
                else
                {
                    if (current is not null) result.Add(current);
                    current = new RecordingSegment { ChannelNumber = group.Key, Start = start, End = end };
                }
            }
            if (current is not null) result.Add(current);
        }
        return result.OrderBy(s => s.Start).ToList();
    }

    public static DateTime? Locate(IReadOnlyList<RecordingSegment> segments, DateTime time, bool backwards)
    {
        if (backwards)
        {
            var segment = segments.LastOrDefault(s => s.Start < time);
            return segment is null ? null : time < segment.End ? time : segment.End.AddMilliseconds(-1);
        }
        var next = segments.FirstOrDefault(s => s.End > time);
        return next is null ? null : time >= next.Start ? time : next.Start;
    }
}
