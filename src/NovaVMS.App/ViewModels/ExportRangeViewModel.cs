using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Models;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Start and end of a recording export, each edited as a date and a typed time of day.
/// Shared by the single camera playback and the playback of several cameras.
/// </summary>
public sealed partial class ExportRangeViewModel : ViewModelBase
{
    [ObservableProperty]
    private DateTime? _fromDate = DateTime.Today;

    [ObservableProperty]
    private DateTime? _toDate = DateTime.Today;

    [ObservableProperty]
    private TimeSpan? _fromTime = TimeSpan.Zero;

    [ObservableProperty]
    private TimeSpan? _toTime = new(23, 59, 59);

    [ObservableProperty]
    private string _fromText = "00:00:00";

    [ObservableProperty]
    private string _toText = "23:59:59";

    /// <summary>True when both ends are set and the start is earlier than the end.</summary>
    public bool IsValid => ExportTimeRange.IsValid(FromDate, FromTime, ToDate, ToTime);

    /// <summary>Start of the range; only meaningful when <see cref="IsValid"/> is true.</summary>
    public DateTime From => FromDate!.Value.Date + FromTime!.Value;

    /// <summary>End of the range; only meaningful when <see cref="IsValid"/> is true.</summary>
    public DateTime To => ToDate!.Value.Date + ToTime!.Value;

    public void SetDay(DateTime? day)
    {
        FromDate = day;
        ToDate = day;
    }

    public void SetStart(DateTime start)
    {
        FromDate = start.Date;
        FromTime = start.TimeOfDay;
    }

    public void SetEnd(DateTime end)
    {
        ToDate = end.Date;
        ToTime = end.TimeOfDay;
    }

    /// <summary>Marks the start at a whole second of a timeline position within a day.</summary>
    public void MarkStart(DateTime day, double positionSeconds)
    {
        FromDate = day;
        FromTime = TimeSpan.FromSeconds(Math.Floor(positionSeconds));
    }

    /// <summary>Marks the end at a whole second of a timeline position within a day.</summary>
    public void MarkEnd(DateTime day, double positionSeconds)
    {
        ToDate = day;
        ToTime = TimeSpan.FromSeconds(Math.Floor(positionSeconds));
    }

    partial void OnFromTextChanged(string value)
    {
        FromTime = ExportTimeRange.Parse(value);
    }

    partial void OnToTextChanged(string value)
    {
        ToTime = ExportTimeRange.Parse(value);
    }

    partial void OnFromTimeChanged(TimeSpan? value)
    {
        if (value.HasValue)
        {
            FromText = ExportTimeRange.Format(value.Value);
        }
    }

    partial void OnToTimeChanged(TimeSpan? value)
    {
        if (value.HasValue)
        {
            ToText = ExportTimeRange.Format(value.Value);
        }
    }
}
