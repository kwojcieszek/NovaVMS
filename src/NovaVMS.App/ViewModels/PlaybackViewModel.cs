using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Searches and plays back recordings of the selected camera within a chosen time range of a day.
/// </summary>
public sealed partial class PlaybackViewModel : ViewModelBase, IDisposable
{
    private const string NoCameraKey = "Playback_NoCamera";
    private const double SecondsPerDay = 86400;
    private const double LastSecondOfDay = SecondsPerDay - 1;

    private static readonly TimeSpan DefaultFromTime = TimeSpan.Zero;
    private static readonly TimeSpan DefaultToTime = new(23, 59, 0);

    private readonly RecordingPlaybackBuffer _buffer;
    private readonly List<Task> _openings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _disposal;
    public Task Disposal => _disposal ?? Task.CompletedTask;
    private readonly LibVLC _libVlc;
    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clientFactory;
    private readonly DispatcherTimer _positionTimer;
    private MediaPlayer _player => _buffer.ActivePlayer;
    private readonly IDialogService _dialogs;
    private readonly RecordingExporter _exporter;
    private CancellationTokenSource? _exportCancellation;
    private Task? _exportTask;
    private int _searchVersion;
    private DateTime _playbackEnd;
    private bool _isOpening;
    private DateTime _shuttleAnchor;
    private long _shuttleStarted;
    private long _previewStarted;
    private long _previewVisibleStarted;
    private VideoMedia? _media => _buffer.ActiveMedia;
    private CameraReference? _camera;
    private DateTime _playbackStart;
    private bool _isShutDown;
    private int _playbackVersion;

    [ObservableProperty]
    private string _cameraName = Localizer.Text(NoCameraKey);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Day))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private DateTime? _selectedDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeStartSeconds))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private TimeSpan? _fromTime = DefaultFromTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeEndSeconds))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private TimeSpan? _toTime = DefaultToTime;

    [ObservableProperty]
    private RecordingSegment? _selectedSegment;

    [ObservableProperty]
    private double _timelinePosition;

    [ObservableProperty]
    private bool _isSeeking;

    [ObservableProperty]
    private string _currentTimeText = "--:--:--";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(SeekCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkExportStartCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkExportEndCommand))]
    private bool _hasCamera;

    [ObservableProperty]
    private PlaybackSpeedOption _selectedSpeed = PlaybackSpeedOption.All.Single(option => option.Rate == 1);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private DateTime? _exportFromDate = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private DateTime? _exportToDate = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private TimeSpan? _exportFromTime = TimeSpan.Zero;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private TimeSpan? _exportToTime = new(23, 59, 59);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelExportCommand))]
    private bool _isExporting;

    [ObservableProperty]
    private double _exportProgress;

    [ObservableProperty]
    private string _exportStatus = string.Empty;

    [ObservableProperty]
    private string _exportFromText = "00:00:00";

    [ObservableProperty]
    private string _exportToText = "23:59:59";

    partial void OnExportFromTextChanged(string value) => ExportFromTime = ParseExportTime(value);
    partial void OnExportToTextChanged(string value) => ExportToTime = ParseExportTime(value);
    partial void OnExportFromTimeChanged(TimeSpan? value)
    {
        if (value.HasValue) ExportFromText = value.Value.ToString(@"hh\:mm\:ss");
    }
    partial void OnExportToTimeChanged(TimeSpan? value)
    {
        if (value.HasValue) ExportToText = value.Value.ToString(@"hh\:mm\:ss");
    }

    internal static TimeSpan? ParseExportTime(string value) =>
        TimeSpan.TryParseExact(value, [@"hh\:mm\:ss", @"hh\:mm"], CultureInfo.InvariantCulture, out var result)
        && result >= TimeSpan.Zero && result < TimeSpan.FromDays(1) ? result : null;

    public IReadOnlyList<PlaybackSpeedOption> Speeds => PlaybackSpeedOption.All;

    public PlaybackViewModel([Microsoft.Extensions.DependencyInjection.FromKeyedServices(VideoPlayerFactory.Recordings)] LibVLC libVlc, ConfigurationService configuration, IDeviceClientFactory clientFactory,
        IDialogService dialogs, RecordingExporter exporter)
    {
        _libVlc = libVlc;
        _configuration = configuration;
        _clientFactory = clientFactory;
        _dialogs = dialogs;
        _exporter = exporter;

        _buffer = new RecordingPlaybackBuffer(libVlc);
        _buffer.ActivePlayerChanged += OnActivePlayerChanged;
        _buffer.PrimaryPlayer.EncounteredError += OnPrimaryPlayerError;
        _buffer.SecondaryPlayer.EncounteredError += OnSecondaryPlayerError;
        _buffer.PrimaryPlayer.EndReached += OnPrimaryPlayerEndReached;
        _buffer.SecondaryPlayer.EndReached += OnSecondaryPlayerEndReached;
        PrimaryPlayer = _buffer.PrimaryPlayer;
        SecondaryPlayer = _buffer.SecondaryPlayer;
        VideoPlayer = _player;

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _positionTimer.Tick += OnPositionTimerTick;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Player shown by the video view. Cleared before the player is disposed, so the view
    /// detaches from a live native player instead of a released one.
    /// </summary>
    public MediaPlayer? VideoPlayer { get; private set; }

    public MediaPlayer? PrimaryPlayer { get; private set; }
    public MediaPlayer? SecondaryPlayer { get; private set; }
    public bool IsPrimaryPlayerActive => ReferenceEquals(_player, _buffer.PrimaryPlayer);

    private void OnActivePlayerChanged(object? sender, EventArgs e)
    {
        VideoPlayer = _player;
        OnPropertyChanged(nameof(VideoPlayer));
        OnPropertyChanged(nameof(IsPrimaryPlayerActive));
    }

    public ObservableCollection<RecordingSegment> Segments { get; } = new();

    public DateTime Day => (SelectedDate ?? DateTime.Today).Date;

    /// <summary>
    /// Start of the searched range in seconds from midnight (used to shade the timeline).
    /// </summary>
    public double RangeStartSeconds => (FromTime ?? DefaultFromTime).TotalSeconds;

    /// <summary>
    /// End of the searched range in seconds from midnight (used to shade the timeline).
    /// </summary>
    public double RangeEndSeconds => Math.Min(SecondsPerDay, (ToTime ?? DefaultToTime).TotalSeconds + 59);

    public void SetCamera(CameraReference? camera)
    {
        if (Equals(_camera, camera) || _isShutDown)
        {
            return;
        }

        StopPlayer();
        _camera = camera;
        _searchVersion++;
        Segments.Clear();

        var descriptor = camera is null ? null : _configuration.FindCamera(camera);
        CameraName = descriptor?.FullName ?? Localizer.Text(NoCameraKey);
        HasCamera = descriptor is not null;
        ExportCommand.NotifyCanExecuteChanged();
        StatusText = string.Empty;

        if (CanSearch())
        {
            SearchCommand.Execute(null);
        }
    }

    /// <summary>
    /// Stops playback before the window and its native video surface are destroyed.
    /// </summary>
    public void Shutdown()
    {
        if (_isShutDown)
        {
            return;
        }

        _isShutDown = true;
        _lifetime.Cancel();
        _exportCancellation?.Cancel();
        StopPlayer();
        SearchCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => _disposal ??= DisposeCoreAsync();

    private async Task DisposeCoreAsync()
    {
        Shutdown();
        try { await Task.WhenAll(_openings); }
        catch (Exception exception) { UnhandledExceptionLogger.Log("Finishing recording operations", exception); }
        if (_exportTask is not null) await _exportTask;
        await _buffer.WhenIdle;
        VideoPlayer = null;
        PrimaryPlayer = null;
        SecondaryPlayer = null;
        OnPropertyChanged(nameof(VideoPlayer));
        OnPropertyChanged(nameof(PrimaryPlayer));
        OnPropertyChanged(nameof(SecondaryPlayer));
        _positionTimer.Tick -= OnPositionTimerTick;
        _buffer.PrimaryPlayer.EncounteredError -= OnPrimaryPlayerError;
        _buffer.SecondaryPlayer.EncounteredError -= OnSecondaryPlayerError;
        _buffer.PrimaryPlayer.EndReached -= OnPrimaryPlayerEndReached;
        _buffer.SecondaryPlayer.EndReached -= OnSecondaryPlayerEndReached;
        _buffer.ActivePlayerChanged -= OnActivePlayerChanged;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        await _buffer.DisposeAsync();
        _lifetime.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task SearchAsync()
    {
        _openings.RemoveAll(task => task.IsCompleted);
        var search = SearchCoreAsync();
        _openings.Add(search);
        return search;
    }

    private async Task SearchCoreAsync()
    {
        var camera = ResolveCamera();
        if (camera is null || _isShutDown)
        {
            return;
        }

        var (from, to) = GetSearchRange();
        var searchVersion = ++_searchVersion;
        StatusText = Localizer.Format("Playback_Searching", from.ToString("HH:mm"), to.ToString("HH:mm"));
        Segments.Clear();

        try
        {
            using var client = await _clientFactory.CreateAsync(camera.Device, cancellationToken: _lifetime.Token);
            if (client is IDeviceConnectionClient connectionClient)
            {
                var connection = await connectionClient.TryConnectAsync(_lifetime.Token);
                if (!connection.Success)
                {
                    StatusText = DeviceOperationErrors.Report("Searching recordings", connection.Error!);
                    return;
                }
            }
            var found = await client.SearchRecordingsAsync(camera.Channel.Number, from, to, _lifetime.Token);
            if (_isShutDown || searchVersion != _searchVersion) return;

            var coverage = RecordingTimeline.Merge(found, from, to);
            foreach (var segment in coverage)
            {
                Segments.Add(segment);
            }

            StatusText = coverage.Count == 0
                ? Localizer.Format("Playback_NoRecordings", from.ToString("HH:mm"), to.ToString("HH:mm"))
                : Localizer.Format("Playback_Found", coverage.Count);
            if (coverage.Count > 0)
            {
                ExportFromDate = coverage[0].Start.Date;
                ExportFromTime = coverage[0].Start.TimeOfDay;
                ExportToDate = coverage[^1].End.Date;
                ExportToTime = coverage[^1].End.TimeOfDay;
            }
        }
        catch (OperationCanceledException) when (_isShutDown) { }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            if (searchVersion != _searchVersion || _isShutDown) return;
            StatusText = DeviceOperationErrors.Report("Searching recordings", exception);
        }
    }

    [RelayCommand]
    private void ResetTimeRange()
    {
        FromTime = DefaultFromTime;
        ToTime = DefaultToTime;
    }

    [RelayCommand(CanExecute = nameof(HasCamera))]
    private async Task SeekAsync(double seconds)
    {
        var start = Day.AddSeconds(Math.Clamp(seconds, 0, LastSecondOfDay));
        if (Segments.Count > 0)
        {
            var located = RecordingTimeline.Locate(Segments, start, SelectedSpeed.Rate < 0);
            if (located is null) { Stop(); return; }
            start = located.Value;
        }
        ResetShuttle(start);
        await PlayFromAsync(start);
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void TogglePause()
    {
        if (_isShutDown) return;
        IsPaused = !IsPaused;
        ResetShuttle(Day.AddSeconds(TimelinePosition));
        _ = _buffer.SetPauseAsync(IsPaused);
        StatusText = Localizer.Text(IsPaused ? "Playback_Paused" : "Playback_Playing");
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void Stop()
    {
        if (_isShutDown) return;
        StopPlayer();
        StatusText = Localizer.Text("Playback_Stopped");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!HasCamera)
        {
            CameraName = Localizer.Text(NoCameraKey);
        }
    }

    private bool CanSearch()
    {
        return !_isShutDown && HasCamera && SelectedDate is not null && FromTime is not null && ToTime is not null && FromTime < ToTime;
    }

    private (DateTime From, DateTime To) GetSearchRange()
    {
        var from = Day + (FromTime ?? DefaultFromTime);
        var to = Day + (ToTime ?? DefaultToTime);

        // The "to" time is inclusive: search up to the last second of the selected minute.
        to = to.AddSeconds(59);
        if (to.TimeOfDay.TotalSeconds > LastSecondOfDay || to.Date > Day)
        {
            to = Day.AddSeconds(LastSecondOfDay);
        }

        return (from, to);
    }

    partial void OnSelectedDateChanged(DateTime? value)
    {
        if (_isShutDown) return;
        StopPlayer();
        _searchVersion++;
        Segments.Clear();
        TimelinePosition = RangeStartSeconds;

        if (CanSearch())
        {
            SearchCommand.Execute(null);
        }
    }

    partial void OnFromTimeChanged(TimeSpan? value)
    {
        ValidateRange();
    }

    partial void OnToTimeChanged(TimeSpan? value)
    {
        ValidateRange();
    }

    partial void OnSelectedSegmentChanged(RecordingSegment? value)
    {
        if (value is null)
        {
            return;
        }

        var (from, _) = GetSearchRange();
        var start = value.Start < from ? from : value.Start;
        ResetShuttle(start);
        _ = PlayFromAsync(start);
    }

    private void ValidateRange()
    {
        if (FromTime is not null && ToTime is not null && FromTime >= ToTime)
        {
            StatusText = Localizer.Text("Playback_InvalidRange");
        }
    }

    private Task PlayFromAsync(DateTime start)
    {
        _openings.RemoveAll(task => task.IsCompleted);
        var opening = PlayFromCoreAsync(start);
        _openings.Add(opening);
        return opening;
    }

    private async Task PlayFromCoreAsync(DateTime start)
    {
        var camera = ResolveCamera();
        if (camera is null || _isShutDown)
        {
            return;
        }

        var version = ++_playbackVersion;
        _buffer.CancelPending();
        _isOpening = true;

        try
        {
            using var client = await _clientFactory.CreateAsync(camera.Device, cancellationToken: _lifetime.Token);
            var end = Segments.FirstOrDefault(segment => segment.Start <= start && segment.End > start)?.End
                ?? start.Date.AddSeconds(LastSecondOfDay);
            var source = await client.GetPlaybackSourceAsync(camera.Channel.Number, start, end, _lifetime.Token);
            if (_isShutDown || version != _playbackVersion) return;
            var caching = _configuration.Current.Settings.GetNetworkCachingMs(StreamType.Main);
            var started = await _buffer.SwitchAsync(() => VideoMedia.Create(_libVlc, source,
                camera.Device.Username, camera.Device.Password, caching,
                withAudio: SelectedSpeed.Rate == 1, paceStream: true), _lifetime.Token);
            if (_isShutDown || version != _playbackVersion) return;
            if (!started) { StatusText = Localizer.Text("Playback_Failed"); return; }

            _playbackStart = start;
            _playbackEnd = end;
            IsPlaying = true;
            TimelinePosition = (start - Day).TotalSeconds;
            CurrentTimeText = start.ToString("HH:mm:ss");
            StatusText = Localizer.Format("Playback_PlayingFrom", start.ToString("HH:mm:ss"));
            _positionTimer.Start();
            _previewStarted = Stopwatch.GetTimestamp();
            _previewVisibleStarted = 0;
        }
        catch (OperationCanceledException) when (_isShutDown) { }
        catch (Exception exception)
        {
            if (_isShutDown || version != _playbackVersion) return;
            StatusText = DeviceOperationErrors.Report("Opening recording playback", exception);
        }
        finally
        {
            if (version == _playbackVersion)
            {
                _isOpening = false;
                if (_media is not null) _positionTimer.Start();
            }
        }
    }

    private void StopPlayer()
    {
        _playbackVersion++;
        _isOpening = false;
        _positionTimer.Stop();
        _buffer.Stop();

        IsPlaying = false;
        IsPaused = false;
    }

    private CameraDescriptor? ResolveCamera()
    {
        return _camera is null ? null : _configuration.FindCamera(_camera);
    }

    private async void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (!IsPlaying || IsSeeking || IsPaused || _isOpening)
        {
            return;
        }

        if (SelectedSpeed.Rate != 1)
        {
            // RTSP and callback streams are not seekable files, and LibVLC does not support
            // negative playback rates. Shuttle by absolute recording time for every protocol.
            // Count the preview interval from its first displayed frame, not Play().
            // SDK buffering can take longer than the interval; reopening immediately
            // after the first frame otherwise keeps the output almost permanently black.
            if (_media?.Media.Statistics.DisplayedPictures > 0)
            {
                if (_previewVisibleStarted == 0) _previewVisibleStarted = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(_previewVisibleStarted) < TimeSpan.FromMilliseconds(900)) return;
            }
            else if (Stopwatch.GetElapsedTime(_previewStarted) < TimeSpan.FromSeconds(4)) return;
            var target = _shuttleAnchor.AddSeconds(Stopwatch.GetElapsedTime(_shuttleStarted).TotalSeconds * SelectedSpeed.Rate);
            if (target < Day || target >= Day.AddDays(1))
            {
                Stop();
                StatusText = Localizer.Text("Playback_End");
                return;
            }
            var located = RecordingTimeline.Locate(Segments, target, SelectedSpeed.Rate < 0);
            if (located is null) { Stop(); StatusText = Localizer.Text("Playback_End"); return; }
            if (located.Value != target) ResetShuttle(located.Value);
            await PlayFromAsync(located.Value);
            return;
        }

        var elapsed = _player.Time;
        if (elapsed < 0)
        {
            return;
        }

        var current = _playbackStart.AddMilliseconds(elapsed);
        TimelinePosition = Math.Clamp((current - Day).TotalSeconds, 0, LastSecondOfDay);
        CurrentTimeText = current.ToString("HH:mm:ss");
        if (current >= _playbackEnd) await ContinuePlaybackAsync(_playbackEnd);
    }

    // LibVLC raises events on its own threads - marshal them to the UI thread.
    private void OnPrimaryPlayerError(object? sender, EventArgs e) => OnPlayerError(_buffer.PrimaryPlayer);
    private void OnSecondaryPlayerError(object? sender, EventArgs e) => OnPlayerError(_buffer.SecondaryPlayer);
    private void OnPrimaryPlayerEndReached(object? sender, EventArgs e) => OnPlayerEndReached(_buffer.PrimaryPlayer);
    private void OnSecondaryPlayerEndReached(object? sender, EventArgs e) => OnPlayerEndReached(_buffer.SecondaryPlayer);

    private void OnPlayerError(MediaPlayer player)
    {
        var version = _playbackVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isShutDown || _media is null || _isOpening || version != _playbackVersion || !ReferenceEquals(player, _player))
            {
                return;
            }

            var message = _media?.ErrorMessage;
            if (message is not null) DeviceOperationErrors.Report("Recording playback", new DeviceCommunicationException(message));
            StopPlayer();
            StatusText = message ?? Localizer.Text("Playback_Failed");
        });
    }

    private void OnPlayerEndReached(MediaPlayer player)
    {
        var version = _playbackVersion;
        Dispatcher.UIThread.Post(async () =>
        {
            if (_isShutDown || _media is null || version != _playbackVersion || _isOpening || !ReferenceEquals(player, _player))
            {
                return;
            }

            var message = _media?.ErrorMessage;
            var current = _playbackStart.AddMilliseconds(Math.Max(0, _player.Time));
            if (message is null && SelectedSpeed.Rate == 1 && current > _playbackStart.AddSeconds(1))
            {
                await ContinuePlaybackAsync(current >= _playbackEnd.AddSeconds(-2) ? _playbackEnd : current.AddSeconds(1));
                return;
            }
            StopPlayer();
            StatusText = message ?? Localizer.Text("Playback_End");
            if (message is not null) DeviceOperationErrors.Report("Recording stream ended", new DeviceCommunicationException(message));
        });
    }

    private async Task ContinuePlaybackAsync(DateTime time)
    {
        var next = RecordingTimeline.Locate(Segments, time, false);
        if (next is { } start) await PlayFromAsync(start);
        else { StopPlayer(); StatusText = Localizer.Text("Playback_End"); }
    }

    private void ResetShuttle(DateTime position)
    {
        _shuttleAnchor = position;
        _shuttleStarted = Stopwatch.GetTimestamp();
    }

    partial void OnSelectedSpeedChanged(PlaybackSpeedOption value)
    {
        ResetShuttle(Day.AddSeconds(TimelinePosition));
    }

    [RelayCommand]
    private void NormalSpeed() => SelectedSpeed = Speeds.Single(option => option.Rate == 1);

    [RelayCommand(CanExecute = nameof(HasCamera))]
    private void MarkExportStart()
    {
        ExportFromDate = Day;
        ExportFromTime = TimeSpan.FromSeconds(Math.Floor(TimelinePosition));
    }

    [RelayCommand(CanExecute = nameof(HasCamera))]
    private void MarkExportEnd()
    {
        ExportToDate = Day;
        ExportToTime = TimeSpan.FromSeconds(Math.Floor(TimelinePosition));
    }

    private bool CanExport() => !_isShutDown && HasCamera && !IsExporting
        && ExportFromDate.HasValue && ExportToDate.HasValue && ExportFromTime.HasValue && ExportToTime.HasValue
        && ExportFromDate.Value.Date + ExportFromTime.Value < ExportToDate.Value.Date + ExportToTime.Value;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportAsync()
    {
        _exportTask = ExportCoreAsync();
        return _exportTask;
    }

    private async Task ExportCoreAsync()
    {
        var camera = ResolveCamera();
        if (camera is null || !CanExport()) return;
        var from = ExportFromDate!.Value.Date + ExportFromTime!.Value;
        var to = ExportToDate!.Value.Date + ExportToTime!.Value;
        IsExporting = true;
        ExportProgress = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _exportCancellation = cancellation;
        try
        {
            var folder = await _dialogs.PickFolderAsync(Localizer.Text("Playback_ExportFolder"));
            if (folder is null) return;
            ExportStatus = Localizer.Text("Playback_ExportPreparing");
            using var client = await _clientFactory.CreateAsync(camera.Device, cancellationToken: cancellation.Token);
            var found = await client.SearchRecordingsAsync(camera.Channel.Number, from, to, cancellation.Token);
            var coverage = RecordingTimeline.Merge(found, from, to);
            if (coverage.Count == 0) { ExportStatus = Localizer.Text("Playback_ExportNoRecordings"); return; }
            var total = coverage.Sum(segment => (segment.End - segment.Start).TotalSeconds);
            var completed = 0d;
            for (var index = 0; index < coverage.Count; index++)
            {
                var segment = coverage[index];
                var duration = segment.End - segment.Start;
                var before = completed;
                var filename = RecordingExporter.FileName(camera.DisplayName, camera.Channel.Number, segment.Start, segment.End);
                var source = await client.GetPlaybackSourceAsync(camera.Channel.Number, segment.Start, segment.End, cancellation.Token);
                var progress = new Progress<double>(value =>
                {
                    if (_exportCancellation != cancellation || cancellation.IsCancellationRequested) return;
                    ExportProgress = (before + value * duration.TotalSeconds) / total * 100;
                });
                ExportStatus = Localizer.Format("Playback_Exporting", index + 1, coverage.Count);
                await _exporter.ExportAsync(source, camera.Device.Username, camera.Device.Password, duration,
                    Path.Combine(folder, filename), progress, cancellation.Token);
                completed += duration.TotalSeconds;
            }
            ExportProgress = 100;
            ExportStatus = Localizer.Format("Playback_ExportComplete", coverage.Count, folder);
        }
        catch (OperationCanceledException) { ExportStatus = Localizer.Text("Playback_ExportCancelled"); }
        catch (Exception exception) { ExportStatus = DeviceOperationErrors.Report("Exporting recordings", exception); }
        finally
        {
            _exportCancellation = null;
            IsExporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsExporting))]
    private void CancelExport() => _exportCancellation?.Cancel();
}
