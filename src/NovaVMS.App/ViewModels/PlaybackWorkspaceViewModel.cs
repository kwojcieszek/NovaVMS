using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using NovaVMS.App.Localization;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>One recording clock and transport for up to eight independent camera sessions.</summary>
public sealed partial class PlaybackWorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly LibVLC _vlc;
    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clients;
    private readonly IDialogService _dialogs;
    private readonly RecordingExporter _exporter;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly List<Task> _work = new();
    private readonly List<Task> _retired = new();
    private readonly HashSet<PlaybackTileViewModel> _failedTiles = new();
    private Task _search = Task.CompletedTask;
    private Task? _disposal;
    private double _anchor;
    private int _version;
    private bool _opening;
    private bool _shutdown;
    private bool _explicitSelection;

    public Task Disposal => _disposal ?? Task.CompletedTask;
    public ObservableCollection<PlaybackTileViewModel> Tiles { get; } = new();
    public IReadOnlyList<int> CameraCounts { get; } = Enumerable.Range(1, 8).ToArray();
    public IReadOnlyList<PlaybackSpeedOption> Speeds => PlaybackSpeedOption.All;
    public int Columns => SelectedCameraCount <= 1 ? 1 : SelectedCameraCount <= 4 ? 2 : SelectedCameraCount <= 6 ? 3 : 4;
    public int Rows => (SelectedCameraCount + Columns - 1) / Columns;
    public DateTime Day => (SelectedDate ?? DateTime.Today).Date;
    public double RangeStartSeconds => (FromTime ?? TimeSpan.Zero).TotalSeconds;
    public double RangeEndSeconds => Math.Min(86400, (ToTime ?? new TimeSpan(23, 59, 0)).TotalSeconds + 59);
    public bool HasCamera => Tiles.Any(tile => tile.Playback.HasCamera);
    public PlaybackViewModel SelectedPlayback => SelectedTile.Playback;
    public string CameraName => SelectedPlayback.CameraName;
    public ObservableCollection<RecordingSegment> Segments => SelectedPlayback.Segments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Columns))]
    [NotifyPropertyChangedFor(nameof(Rows))]
    private int _selectedCameraCount = 1;

    [ObservableProperty]
    private PlaybackTileViewModel _selectedTile = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Day))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private DateTime? _selectedDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeStartSeconds))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private TimeSpan? _fromTime = TimeSpan.Zero;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeEndSeconds))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private TimeSpan? _toTime = new(23, 59, 0);

    [ObservableProperty]
    private double _timelinePosition;

    [ObservableProperty]
    private string _currentTimeText = "00:00:00";

    [ObservableProperty]
    private bool _isSeeking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private PlaybackSpeedOption _selectedSpeed = PlaybackSpeedOption.All.Single(speed => speed.Rate == 1);

    [ObservableProperty]
    private bool _isExportPanelExpanded = true;

    public event EventHandler<RecordingOpeningEventArgs>? RecordingOpening;

    public PlaybackWorkspaceViewModel(
        [Microsoft.Extensions.DependencyInjection.FromKeyedServices(VideoPlayerFactory.Recordings)] LibVLC vlc,
        ConfigurationService configuration, IDeviceClientFactory clients, IDialogService dialogs, RecordingExporter exporter)
    {
        _vlc = vlc; _configuration = configuration; _clients = clients; _dialogs = dialogs; _exporter = exporter;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
        AddTile();
        SelectedTile = Tiles[0];
    }

    private void AddTile()
    {
        var session = new PlaybackViewModel(_vlc, _configuration, _clients, _dialogs, _exporter) { IsExternallyControlled = true };
        session.ConfigureSynchronizedRange(SelectedDate, FromTime, ToTime);
        session.RecordingOpening += OnRecordingOpening;
        session.PropertyChanged += OnSessionChanged;
        Tiles.Add(new PlaybackTileViewModel(Tiles.Count + 1, session, this));
    }

    partial void OnSelectedCameraCountChanged(int value)
    {
        var count = Math.Clamp(value, 1, 8);
        if (count != value) { SelectedCameraCount = count; return; }
        if (_shutdown) return;
        var resume = IsPlaying;
        var paused = IsPaused;
        var position = TimelinePosition;
        Stop();
        while (Tiles.Count < count) AddTile();
        while (Tiles.Count > count)
        {
            var tile = Tiles[^1];
            tile.Playback.StopSynchronized();
            Tiles.RemoveAt(Tiles.Count - 1);
            _failedTiles.Remove(tile);
            Retire(tile);
        }
        if (!Tiles.Contains(SelectedTile)) SelectedTile = Tiles[0];
        NotifyCamerasChanged();
        if (resume && HasCamera)
        {
            IsPaused = paused;
            Track(SynchronizeAsync(position));
        }
    }

    public void SelectTile(PlaybackTileViewModel tile)
    {
        if (!Tiles.Contains(tile) || _shutdown) return;
        SelectedTile = tile;
        _explicitSelection = true;
    }

    partial void OnSelectedTileChanged(PlaybackTileViewModel? oldValue, PlaybackTileViewModel newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        newValue.IsSelected = true;
        newValue.Playback.TimelinePosition = TimelinePosition;
        OnPropertyChanged(nameof(SelectedPlayback));
        OnPropertyChanged(nameof(CameraName));
        OnPropertyChanged(nameof(Segments));
        foreach (var tile in Tiles) Track(tile.Playback.SetSynchronizedMuteAsync(!tile.IsSelected));
    }

    public void SetCamera(CameraReference? camera)
    {
        if (_shutdown) return;
        if (camera is null) { ClearCamera(SelectedTile); return; }
        var existing = Tiles.FirstOrDefault(tile => Equals(tile.Camera, camera));
        if (existing is not null) { SelectedTile = existing; return; }
        var target = !_explicitSelection && SelectedTile.Camera is not null
            ? Tiles.FirstOrDefault(tile => tile.Camera is null) ?? SelectedTile : SelectedTile;
        SelectedTile = target;
        _explicitSelection = false;
        target.Camera = camera;
        target.IsExportSelected = true;
        target.Playback.SetCamera(camera);
        _failedTiles.Remove(target);
        NotifyCamerasChanged();
        var search = target.Playback.SearchCommand.ExecutionTask ?? Task.CompletedTask;
        _search = Task.WhenAll(_search, search);
        Track(JoinCameraAsync(target, camera, search));
    }

    public void SetCameras(IReadOnlyList<CameraReference> cameras)
    {
        if (_shutdown || cameras.Count == 0) return;
        Stop();
        SelectedCameraCount = Math.Clamp(cameras.Distinct().Count(), 1, 8);
        foreach (var tile in Tiles) ClearCamera(tile);
        _search = Task.CompletedTask;
        SelectedTile = Tiles[0];
        _explicitSelection = false;
        foreach (var camera in cameras.Distinct().Take(8)) SetCamera(camera);
    }

    private async Task JoinCameraAsync(PlaybackTileViewModel tile, CameraReference camera, Task search)
    {
        await search;
        if (!_shutdown && IsPlaying && Equals(tile.Camera, camera) && Tiles.Contains(tile))
            await SynchronizeAsync(TimelinePosition);
    }

    public void ClearCamera(PlaybackTileViewModel tile)
    {
        if (_shutdown || !Tiles.Contains(tile)) return;
        tile.Camera = null;
        tile.IsExportSelected = false;
        tile.Playback.SetCamera(null);
        _failedTiles.Remove(tile);
        NotifyCamerasChanged();
        if (!HasCamera) Stop();
    }

    private void NotifyCamerasChanged()
    {
        OnPropertyChanged(nameof(HasCamera));
        OnPropertyChanged(nameof(CameraName));
        SearchCommand.NotifyCanExecuteChanged();
        SeekCommand.NotifyCanExecuteChanged();
        MarkExportStartCommand.NotifyCanExecuteChanged();
        MarkExportEndCommand.NotifyCanExecuteChanged();
        RefreshExportSelection();
    }

    private void OnRecordingOpening(object? sender, RecordingOpeningEventArgs args) => RecordingOpening?.Invoke(this, args);
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (SelectedTile is not null && ReferenceEquals(sender, SelectedPlayback) && args.PropertyName == nameof(PlaybackViewModel.CameraName))
            OnPropertyChanged(nameof(CameraName));
    }

    private bool CanSearch() => !_shutdown && HasCamera && SelectedDate.HasValue && FromTime.HasValue && ToTime.HasValue && FromTime < ToTime;
    private bool CanSeek() => CanSearch();

    [RelayCommand(CanExecute = nameof(CanSearch), AllowConcurrentExecutions = true)]
    private Task SearchAsync()
    {
        Stop();
        foreach (var tile in Tiles) tile.Playback.ConfigureSynchronizedRange(SelectedDate, FromTime, ToTime);
        _search = Task.WhenAll(Tiles.Where(tile => tile.Playback.SearchCommand.CanExecute(null))
            .Select(tile => tile.Playback.SearchCommand.ExecuteAsync(null)));
        Track(_search);
        return _search;
    }

    partial void OnSelectedDateChanged(DateTime? value)
    {
        ExportFromDate = ExportToDate = value;
        RangeChanged();
    }
    partial void OnFromTimeChanged(TimeSpan? value) => RangeChanged();
    partial void OnToTimeChanged(TimeSpan? value) => RangeChanged();
    private void RangeChanged()
    {
        if (_shutdown) return;
        Stop();
        TimelinePosition = RangeStartSeconds;
        foreach (var tile in Tiles) tile.Playback.ConfigureSynchronizedRange(SelectedDate, FromTime, ToTime);
        if (CanSearch()) SearchCommand.Execute(null);
        else StatusText = Localizer.Text("Playback_InvalidRange");
    }

    [RelayCommand]
    private void ResetTimeRange() { FromTime = TimeSpan.Zero; ToTime = new(23, 59, 0); }

    [RelayCommand(CanExecute = nameof(CanSeek), AllowConcurrentExecutions = true)]
    private Task SeekAsync(double seconds)
    {
        if (!double.IsFinite(seconds) || !CanSeek()) return Task.CompletedTask;
        var seek = SynchronizeAsync(Math.Clamp(seconds, RangeStartSeconds, Math.Max(RangeStartSeconds, RangeEndSeconds - 1)));
        Track(seek);
        return seek;
    }

    private async Task SynchronizeAsync(double seconds, bool onlyMissing = false)
    {
        var version = ++_version;
        _opening = true;
        _clock.Stop();
        _timer.Stop();
        IsPlaying = true;
        StatusText = Localizer.Text("Playback_BufferingCameras");
        try
        {
            await _search;
            if (_shutdown || version != _version) return;
            var tiles = Tiles.Where(tile => tile.Playback.HasCamera).ToArray();
            await Task.WhenAll(tiles.Select(tile => tile.Playback.SetSynchronizedPauseAsync(true)));
            if (_shutdown || version != _version) return;
            var time = Day.AddSeconds(seconds);
            if (!onlyMissing) _failedTiles.Clear();
            await Task.WhenAll(tiles.Select(async tile =>
            {
                if (onlyMissing && _failedTiles.Contains(tile)) return;
                tile.Playback.SelectedSpeed = SelectedSpeed;
                await tile.Playback.SetSynchronizedMuteAsync(!tile.IsSelected);
                if (!onlyMissing || !tile.Playback.HasSourceAt(time))
                    await tile.Playback.OpenSynchronizedAsync(seconds);
            }));
            if (_shutdown || version != _version) return;
            foreach (var tile in tiles)
                if (tile.Playback.HasRecordingAt(time) && !tile.Playback.IsPlaying) _failedTiles.Add(tile);
            TimelinePosition = seconds;
            _anchor = seconds;
            await Task.WhenAll(tiles.Where(tile => tile.Playback.IsPlaying)
                .Select(tile => tile.Playback.SetSynchronizedPauseAsync(IsPaused)));
            if (_shutdown || version != _version) return;
            _clock.Restart();
            if (IsPaused) _clock.Stop();
            StatusText = Localizer.Text(IsPaused ? "Playback_Paused" : "Playback_Playing");
        }
        catch (Exception exception)
        {
            if (!_shutdown && version == _version)
            {
                Stop();
                StatusText = DeviceOperationErrors.Report("Synchronized recording playback", exception);
            }
        }
        finally
        {
            if (version == _version)
            {
                _opening = false;
                if (!_shutdown && IsPlaying) _timer.Start();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private async Task TogglePauseAsync()
    {
        if (_shutdown) return;
        IsPaused = !IsPaused;
        if (IsPaused) _clock.Stop(); else if (!_opening) _clock.Start();
        if (!_opening)
            await Task.WhenAll(Tiles.Where(tile => tile.Playback.IsPlaying).Select(tile => tile.Playback.SetSynchronizedPauseAsync(IsPaused)));
        StatusText = Localizer.Text(IsPaused ? "Playback_Paused" : "Playback_Playing");
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void Stop()
    {
        _version++;
        _opening = false;
        _timer.Stop();
        _clock.Reset();
        foreach (var tile in Tiles) tile.Playback.StopSynchronized();
        _failedTiles.Clear();
        IsPlaying = false;
        IsPaused = false;
        StatusText = Localizer.Text("Playback_Stopped");
    }

    partial void OnTimelinePositionChanged(double value)
    {
        if (!double.IsFinite(value)) return;
        CurrentTimeText = Day.AddSeconds(Math.Clamp(value, 0, 86399)).ToString("HH:mm:ss");
        foreach (var tile in Tiles) tile.Playback.TimelinePosition = value;
    }

    partial void OnSelectedSpeedChanged(PlaybackSpeedOption value)
    {
        if (IsPlaying && !_shutdown) Track(SynchronizeAsync(TimelinePosition));
    }

    [RelayCommand]
    private void NormalSpeed() => SelectedSpeed = Speeds.Single(speed => speed.Rate == 1);

    private async void OnTick(object? sender, EventArgs args)
    {
        if (_shutdown || !IsPlaying || IsPaused || IsSeeking || _opening) return;
        try
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            if (SelectedSpeed.Rate != 1 && elapsed < 1) return;
            var position = _anchor + elapsed * SelectedSpeed.Rate;
            if (position < RangeStartSeconds || position >= RangeEndSeconds)
            {
                Stop(); StatusText = Localizer.Text("Playback_End"); return;
            }
            if (SelectedSpeed.Rate != 1) { await SeekAsync(position); return; }
            TimelinePosition = position;
            var time = Day.AddSeconds(position);
            foreach (var tile in Tiles.Where(tile => tile.Playback.IsPlaying && !tile.Playback.HasRecordingAt(time)))
                tile.Playback.StopSynchronized(position);
            if (Tiles.Any(tile => tile.Playback.HasCamera && !_failedTiles.Contains(tile)
                && tile.Playback.HasRecordingAt(time) && !tile.Playback.HasSourceAt(time)))
            {
                var task = SynchronizeAsync(position, onlyMissing: true);
                Track(task);
                await task;
            }
            foreach (var tile in Tiles.Where(tile => !tile.Playback.HasRecordingAt(time))) _failedTiles.Remove(tile);
        }
        catch (Exception exception) { Stop(); StatusText = DeviceOperationErrors.Report("Synchronized recording playback", exception); }
    }

    private void Track(Task task)
    {
        _work.RemoveAll(item => item.IsCompleted);
        _work.Add(ObserveAsync(task));
    }

    private async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch (Exception exception) { if (!_shutdown) StatusText = DeviceOperationErrors.Report("Recording workspace", exception); }
    }

    private void Retire(PlaybackTileViewModel tile)
    {
        tile.Playback.RecordingOpening -= OnRecordingOpening;
        tile.Playback.PropertyChanged -= OnSessionChanged;
        tile.Playback.Dispose();
        _retired.RemoveAll(task => task.IsCompleted);
        _retired.Add(tile.Playback.Disposal);
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        Stop();
        _shutdown = true;
        _exportCancellation?.Cancel();
        foreach (var tile in Tiles) tile.Playback.Shutdown();
        _timer.Tick -= OnTick;
    }

    public void Dispose() => _disposal ??= DisposeCoreAsync();
    private async Task DisposeCoreAsync()
    {
        Shutdown();
        foreach (var tile in Tiles) Retire(tile);
        await Task.WhenAll(_work.Concat(_retired).ToArray());
    }
}
