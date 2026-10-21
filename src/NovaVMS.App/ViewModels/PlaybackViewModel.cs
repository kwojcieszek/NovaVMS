using System.Collections.ObjectModel;
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

    private readonly AsyncWorkQueue _operations = new();
    private readonly List<Task> _openings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _disposal;
    public Task Disposal => _disposal ?? Task.CompletedTask;
    private readonly LibVLC _libVlc;
    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clientFactory;
    private readonly DispatcherTimer _positionTimer;
    private readonly MediaPlayer _player;
    private VideoMedia? _media;
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
    private bool _hasCamera;

    public PlaybackViewModel(LibVLC libVlc, ConfigurationService configuration, IDeviceClientFactory clientFactory)
    {
        _libVlc = libVlc;
        _configuration = configuration;
        _clientFactory = clientFactory;

        _player = new MediaPlayer(libVlc);
        _player.EncounteredError += OnPlayerError;
        _player.EndReached += OnPlayerEndReached;
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
        Segments.Clear();

        var descriptor = camera is null ? null : _configuration.FindCamera(camera);
        CameraName = descriptor?.FullName ?? Localizer.Text(NoCameraKey);
        HasCamera = descriptor is not null;
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
        StopPlayer();
        SearchCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => _disposal ??= DisposeCoreAsync();

    private async Task DisposeCoreAsync()
    {
        Shutdown();
        try { await Task.WhenAll(_openings); }
        catch (Exception exception) { UnhandledExceptionLogger.Log("Finishing recording operations", exception); }
        await _operations.WhenIdle;
        VideoPlayer = null;
        OnPropertyChanged(nameof(VideoPlayer));
        _positionTimer.Tick -= OnPositionTimerTick;
        _player.EncounteredError -= OnPlayerError;
        _player.EndReached -= OnPlayerEndReached;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        await _operations.RunAsync(_player.Dispose);
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
            if (_isShutDown || !Equals(_camera, new CameraReference { DeviceId = camera.Device.Id, ChannelNumber = camera.Channel.Number })) return;

            foreach (var segment in found)
            {
                Segments.Add(segment);
            }

            StatusText = found.Count == 0
                ? Localizer.Format("Playback_NoRecordings", from.ToString("HH:mm"), to.ToString("HH:mm"))
                : Localizer.Format("Playback_Found", found.Count);
        }
        catch (OperationCanceledException) when (_isShutDown) { }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
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
        await PlayFromAsync(Day.AddSeconds(Math.Clamp(seconds, 0, LastSecondOfDay)));
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void TogglePause()
    {
        if (_isShutDown) return;
        IsPaused = !IsPaused;
        var paused = IsPaused;
        var version = _playbackVersion;
        _ = _operations.RunAsync(() => { if (!_isShutDown && version == _playbackVersion) _player.SetPause(paused); });
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
        _ = PlayFromAsync(value.Start < from ? from : value.Start);
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

        StopPlayer();
        var version = _playbackVersion;

        try
        {
            using var client = await _clientFactory.CreateAsync(camera.Device, cancellationToken: _lifetime.Token);
            var end = start.Date.AddSeconds(LastSecondOfDay);
            var source = await client.GetPlaybackSourceAsync(camera.Channel.Number, start, end, _lifetime.Token);
            if (_isShutDown || version != _playbackVersion) return;
            var caching = _configuration.Current.Settings.GetNetworkCachingMs(StreamType.Main);
            var media = await _operations.RunAsync(() => VideoMedia.Create(_libVlc, source, camera.Device.Username, camera.Device.Password, caching));
            if (_isShutDown || version != _playbackVersion)
            {
                await _operations.RunAsync(media.Dispose);
                return;
            }
            _media = media;

            _playbackStart = start;
            var started = await _operations.RunAsync(() => !_isShutDown && version == _playbackVersion && _player.Play(media.Media));
            if (_isShutDown || version != _playbackVersion) return;
            if (!started) { StopPlayer(); StatusText = Localizer.Text("Playback_Failed"); return; }

            IsPlaying = true;
            IsPaused = false;
            TimelinePosition = (start - Day).TotalSeconds;
            CurrentTimeText = start.ToString("HH:mm:ss");
            StatusText = Localizer.Format("Playback_PlayingFrom", start.ToString("HH:mm:ss"));
            _positionTimer.Start();
        }
        catch (OperationCanceledException) when (_isShutDown) { }
        catch (Exception exception)
        {
            if (_isShutDown || version != _playbackVersion) return;
            StatusText = DeviceOperationErrors.Report("Opening recording playback", exception);
        }
    }

    private void StopPlayer()
    {
        _playbackVersion++;
        _positionTimer.Stop();
        var media = _media;
        _media = null;
        _ = _operations.RunAsync(() => VideoMedia.Stop(_player, media));

        IsPlaying = false;
        IsPaused = false;
    }

    private CameraDescriptor? ResolveCamera()
    {
        return _camera is null ? null : _configuration.FindCamera(_camera);
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (!IsPlaying || IsSeeking)
        {
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
    }

    // LibVLC raises events on its own threads - marshal them to the UI thread.
    private void OnPlayerError(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isShutDown || _media is null)
            {
                return;
            }

            var message = _media?.ErrorMessage;
            if (message is not null) DeviceOperationErrors.Report("Recording playback", new DeviceCommunicationException(message));
            StopPlayer();
            StatusText = message ?? Localizer.Text("Playback_Failed");
        });
    }

    private void OnPlayerEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isShutDown || _media is null)
            {
                return;
            }

            var message = _media?.ErrorMessage;
            StopPlayer();
            StatusText = message ?? Localizer.Text("Playback_End");
            if (message is not null) DeviceOperationErrors.Report("Recording stream ended", new DeviceCommunicationException(message));
        });
    }
}
