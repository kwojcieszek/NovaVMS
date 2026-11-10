using System.Globalization;

namespace NovaVMS.App.Models;

/// <summary>
/// Times of the recording export form (HH:mm:ss or HH:mm within one day), shared by the single camera playback
/// and the playback of several cameras.
/// </summary>
internal static class ExportTimeRange
{
    private const string TextFormat = @"hh\:mm\:ss";

    private static readonly string[] AcceptedFormats = [@"hh\:mm\:ss", @"hh\:mm"];

    /// <summary>The time typed in the form; null when it is not a time of day.</summary>
    public static TimeSpan? Parse(string value)
    {
        return TimeSpan.TryParseExact(value, AcceptedFormats, CultureInfo.InvariantCulture, out var result)
            && result >= TimeSpan.Zero && result < TimeSpan.FromDays(1)
                ? result
                : null;
    }

    public static string Format(TimeSpan value)
    {
        return value.ToString(TextFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>True when both ends are set and the start is earlier than the end.</summary>
    public static bool IsValid(DateTime? fromDate, TimeSpan? fromTime, DateTime? toDate, TimeSpan? toTime)
    {
        return fromDate.HasValue && toDate.HasValue && fromTime.HasValue && toTime.HasValue
            && fromDate.Value.Date + fromTime.Value < toDate.Value.Date + toTime.Value;
    }
}
