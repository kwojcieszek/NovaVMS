using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Recordings of one day in the recorder's local time: the day chosen with the day buttons or the calendar,
/// its segments on the 24 hour bar and the time being played.
/// </summary>
public sealed partial class RecordingsViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly CameraViewModel _camera;
    private readonly IDeviceClient _client;
    private readonly Channel _channel;
    private DateTime? _loadedDay;

    /// <summary>Day shown on the bar; a later day than today has no recordings yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDate))]
    [NotifyPropertyChangedFor(nameof(DayText))]
    [NotifyCanExecuteChangedFor(nameof(NextDayCommand))]
    private DateTime _day = DateTime.Today;

    /// <summary>Segments of <see cref="Day"/>, ordered by their start.</summary>
    [ObservableProperty]
    private IReadOnlyList<RecordingSegment> _segments = [];

    /// <summary>Recorder time being played; null while the live picture is shown.</summary>
    [ObservableProperty]
    private DateTime? _position;

    /// <summary>The calendar opened by a tap on the date; choosing a day closes it.</summary>
    [ObservableProperty]
    private bool _isCalendarOpen;

    public ObservableCollection<RecordingItemViewModel> Results { get; } = new();

    /// <summary>The calendar works with an optional date; clearing it keeps the shown day.</summary>
    public DateTime? SelectedDate
    {
        get => Day;
        set
        {
            if (value is { } date)
            {
                Day = date.Date > DateTime.Today ? DateTime.Today : date.Date;
            }

            IsCalendarOpen = false;
        }
    }

    /// <summary>Last day the calendar offers; later days have no recordings yet.</summary>
    public DateTime Today => DateTime.Today;

    public string DayText => Day.ToString("ddd, d MMM yyyy", Localizer.Instance.Culture);

    public RecordingsViewModel(ShellViewModel shell, CameraViewModel camera, IDeviceClient client, Channel channel)
    {
        _shell = shell;
        _camera = camera;
        _client = client;
        _channel = channel;
    }

    /// <summary>Reads the segments of the shown day unless they are already read.</summary>
    internal async Task LoadAsync(CancellationToken token, bool force = false)
    {
        if (!_shell.IsCurrentClient(_client))
        {
            return;
        }

        var day = Day;

        if (!force && _loadedDay == day)
        {
            return;
        }

        var recordings = await _client.SearchRecordingsAsync(_channel.Number, day, day.AddDays(1), token);

        token.ThrowIfCancellationRequested();

        if (day != Day)
        {
            // Another day was chosen meanwhile; its own search follows.
            return;
        }

        var ordered = recordings.OrderBy(recording => recording.Start).ToList();

        Segments = ordered;

        Results.Clear();

        foreach (var recording in ordered)
        {
            Results.Add(new RecordingItemViewModel(_shell, _camera, recording));
        }

        _loadedDay = day;
    }

    /// <summary>First moment with a recording at or after <paramref name="time"/> on the shown day; null when nothing follows.</summary>
    internal DateTime? FindRecordedTime(DateTime time)
    {
        var segment = Segments.FirstOrDefault(recording => recording.End > time);

        if (segment is null)
        {
            return null;
        }

        var start = segment.Start > time ? segment.Start : time;

        return start < Day ? Day : start;
    }

    /// <summary>A tap on the hour bar plays from that moment, or from the next recording when nothing was recorded then.</summary>
    [RelayCommand]
    private Task PlayAtAsync(DateTime time)
    {
        _camera.KeepOverlay();

        return _shell.RunAsync(async token =>
        {
            await LoadAsync(token);

            if (FindRecordedTime(time) is not { } start)
            {
                _camera.ShowMessage(Localizer.Text("Mobile_NoRecordingsAfter"));

                return;
            }

            await _camera.PlayFromAsync(start, token);
        });
    }

    [RelayCommand]
    private void ToggleCalendar()
    {
        _camera.KeepOverlay();
        OnPropertyChanged(nameof(Today));

        IsCalendarOpen = !IsCalendarOpen;
    }

    [RelayCommand]
    private void PreviousDay()
    {
        _camera.KeepOverlay();

        Day = Day.AddDays(-1);
    }

    [RelayCommand(CanExecute = nameof(CanShowNextDay))]
    private void NextDay()
    {
        _camera.KeepOverlay();

        Day = Day.AddDays(1);
    }

    private bool CanShowNextDay()
    {
        return Day < DateTime.Today;
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        return _shell.RunAsync(async token =>
        {
            await LoadAsync(token, force: true);

            _shell.Status = Segments.Count == 0
                ? Localizer.Text("Mobile_NoRecordings")
                : Localizer.Format("Mobile_RecordingsFound", Segments.Count);
        });
    }

    partial void OnDayChanged(DateTime value)
    {
        Segments = [];
        _loadedDay = null;

        Results.Clear();

        _ = _shell.RunAsync(token => LoadAsync(token));
    }
}
