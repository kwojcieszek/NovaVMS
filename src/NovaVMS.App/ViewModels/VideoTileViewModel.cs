using NovaVMS.Core.Exceptions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;
using System.Diagnostics;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Single live view tile with automatic reconnection.
/// </summary>
public sealed partial class VideoTileViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SwitchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SurfaceHandoverDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SurfaceHandoverTimeout = TimeSpan.FromSeconds(2);
    private readonly AsyncWorkQueue _operations = new();
    private readonly List<Task> _openings = new();
    private int _streamVersion;
    private int _switchVersion;
    private Task? _disposal;
    public Task Disposal => _disposal ?? Task.CompletedTask;
    private readonly LibVLC _libVlc;
    private readonly ITileHost _host;
    private readonly MediaPlayer _primaryPlayer;
    private readonly MediaPlayer _secondaryPlayer;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _switchTimer;
    private readonly DispatcherTimer _hiddenTimer;
    // Player shown in the tile; the other one opens the next stream during a seamless switch.
    private MediaPlayer _player;
    private VideoMedia? _switchMedia;
    private TilePlayRequest? _switchRequest;
    private long _switchStartedAt;
    private MediaPlayer? _retiringPlayer;
    private VideoMedia? _retiringMedia;
    private TilePlayRequest? _retiringRequest;
    private long _handoverStartedAt;
    private int _handoverDisplayedFrames;
    private bool _isAspectRatioReported;
    private readonly VideoDisplayGeometry _displayGeometry = new();
    private long _lastFrameAt;
    private int _lastDisplayedFrames;
    private bool _softwareFallback;
    private VideoMedia? _media;
    private TilePlayRequest? _request;
    private bool _isDisposed;
    private bool _isViewReady;
    private bool _isStartPending;

    [ObservableProperty]
    private string _header;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(OverlayText))]
    [NotifyPropertyChangedFor(nameof(IsOverlayTextVisible))]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private TileStatus _status = TileStatus.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayText))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayText))]
    private string? _errorDetails;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAudioEnabled;

    [ObservableProperty]
    private bool _isVisibleInGrid = true;

    [ObservableProperty]
    private bool _showTitle = true;

    public VideoTileViewModel(LibVLC libVlc, int index, ITileHost host)
    {
        _libVlc = libVlc;
        _host = host;
        Index = index;

        _primaryPlayer = CreatePlayer(libVlc);
        _secondaryPlayer = CreatePlayer(libVlc);
        _player = _primaryPlayer;
        PrimaryPlayer = _primaryPlayer;
        SecondaryPlayer = _secondaryPlayer;

        _reconnectTimer = new DispatcherTimer
        {
            Interval = ReconnectDelay
        };
        _reconnectTimer.Tick += OnReconnectTick;
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _progressTimer.Tick += OnProgressTick;
        _switchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _switchTimer.Tick += OnSwitchTick;
        _hiddenTimer = new DispatcherTimer();
        _hiddenTimer.Tick += OnHiddenTick;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        _header = Number.ToString();
    }

    public int Index { get; }

    public double VideoHeight { get; private set; }

    public void SetVideoHeight(double height)
    {
        if (_isDisposed || !double.IsFinite(height) || height <= 0 || Math.Abs(VideoHeight - height) < 0.5) return;
        VideoHeight = height;
        _host.ReportVideoHeight(this);
    }

    /// <summary>
    /// A hidden tile keeps its stream for this time, so restoring the split or returning to the page is instant;
    /// after that the connection is released and reopened when the tile is shown again.
    /// </summary>
    public TimeSpan HiddenStreamTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal int PlayRequestVersion { get; private set; }

    internal int ReservePlayRequest() => ++PlayRequestVersion;

    public int Number => Index + 1;

    /// <summary>
    /// Players shown by the two video views of the tile. Cleared before the players are disposed, so the
    /// views detach from live native players instead of released ones.
    /// </summary>
    public MediaPlayer? PrimaryPlayer { get; private set; }

    public MediaPlayer? SecondaryPlayer { get; private set; }

    public bool IsPrimaryPlayerActive => ReferenceEquals(_player, _primaryPlayer);

    public bool IsPrimaryViewVisible => IsVisibleInGrid
        && (OperatingSystem.IsWindows() || ReferenceEquals(_player, _primaryPlayer) || ReferenceEquals(_retiringPlayer, _primaryPlayer));

    public bool IsSecondaryViewVisible => IsVisibleInGrid
        && (OperatingSystem.IsWindows() || ReferenceEquals(_player, _secondaryPlayer) || ReferenceEquals(_retiringPlayer, _secondaryPlayer));

    public CameraReference? Camera { get; private set; }

    public string? CameraName { get; private set; }

    public bool HasCamera => Camera is not null;

    public bool HasError => Status == TileStatus.Error;

    public bool IsLoading => Status == TileStatus.Connecting;

    public void ShowLoading()
    {
        if (!_isDisposed && Status != TileStatus.Playing) Status = TileStatus.Connecting;
    }

    public StreamType? ActiveStreamType => (_switchRequest ?? _request)?.StreamType;

    /// <summary>
    /// Text drawn over the video area while no picture is shown.
    /// </summary>
    public string OverlayText
    {
        get
        {
            switch (Status)
            {
                case TileStatus.Connecting:
                {
                    return string.Empty;
                }
                case TileStatus.Error:
                {
                    return $"{ErrorDetails}\n{StatusText}";
                }
                default:
                {
                    return string.Empty;
                }
            }
        }
    }

    public bool IsOverlayTextVisible => Status == TileStatus.Error;

    public void Play(CameraReference camera, string cameraName, TilePlayRequest request)
    {
        if (_isDisposed) return;
        var isPlayingCamera = Equals(Camera, camera) && Status == TileStatus.Playing && _isViewReady && IsVisibleInGrid
            && _request is not null && !_isStartPending;
        if (isPlayingCamera && _request!.StreamType == request.StreamType)
        {
            // Back to the stream that is still shown (e.g. restored before the switch completed).
            if (_retiringPlayer is null) CancelSwitch();
            return;
        }

        if (isPlayingCamera && _retiringRequest?.StreamType == request.StreamType)
        {
            // The previous stream is still alive; reuse it when maximizing is reversed
            // before the surface handover has finished.
            RestoreRetiringPlayer(reportFailure: false);
            return;
        }

        if (isPlayingCamera)
        {
            // Another stream of the same camera: keep showing the current picture until the new one is ready.
            BeginSwitch(request);
            return;
        }

        StopPlayer();

        if (!Equals(Camera, camera))
        {
            _softwareFallback = false;
            _displayGeometry.Reset(request.AspectRatio);
        }
        Camera = camera;
        CameraName = cameraName;
        _request = request;
        Header = BuildHeader(cameraName);
        OnPropertyChanged(nameof(HasCamera));

        // A tile created together with a new split has no native video surface yet. Starting LibVLC
        // without it makes VLC render into its own hidden window, so the start waits for the view.
        _isStartPending = true;
        Status = TileStatus.Connecting;
        QueuePendingStart();
    }

    /// <summary>
    /// Called by the view when its native video surface is created (true) or destroyed (false).
    /// </summary>
    public void SetViewReady(bool isReady)
    {
        if (_isDisposed || _isViewReady == isReady) return;
        _isViewReady = isReady;
        if (!isReady)
        {
            StopPlayer();
            _isStartPending = _request is not null;
        }
        else QueuePendingStart();
    }

    private void QueuePendingStart()
    {
        // A split/maximize changes the native surface size. Start after its layout has completed.
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed || !_isViewReady || !IsVisibleInGrid || !_isStartPending || _request is null) return;
            _isStartPending = false;
            StartPlayback();
        }, DispatcherPriority.Background);
    }

    partial void OnIsVisibleInGridChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPrimaryViewVisible));
        OnPropertyChanged(nameof(IsSecondaryViewVisible));
        if (_isDisposed) return;
        // A hidden tile keeps playing for a grace period, so restoring the split is instant
        // and maximizing does not wait for every other player to stop.
        if (value)
        {
            _hiddenTimer.Stop();
            _lastFrameAt = Stopwatch.GetTimestamp();
            QueuePendingStart();
        }
        else if (_request is not null)
        {
            _hiddenTimer.Stop();
            if (HiddenStreamTimeout <= TimeSpan.Zero)
            {
                OnHiddenTick(this, EventArgs.Empty);
                return;
            }

            _hiddenTimer.Interval = HiddenStreamTimeout;
            _hiddenTimer.Start();
        }
    }

    private void OnHiddenTick(object? sender, EventArgs e)
    {
        _hiddenTimer.Stop();
        if (_isDisposed || IsVisibleInGrid || _request is null || _isStartPending)
        {
            return;
        }

        // Release the connection and decoder of a stream nobody sees; showing the tile reopens it.
        StopPlayer();
        _isStartPending = true;
        Status = TileStatus.Connecting;
        StatusText = string.Empty;
        ErrorDetails = null;
    }

    /// <summary>
    /// Updates the camera name shown in the header without restarting the stream.
    /// </summary>
    public void Rename(string cameraName)
    {
        if (Camera is null)
        {
            return;
        }

        CameraName = cameraName;
        Header = BuildHeader(cameraName);
    }

    public void Close()
    {
        if (_isDisposed) return;
        PlayRequestVersion++;
        _isStartPending = false;
        _hiddenTimer.Stop();
        StopPlayer();

        Camera = null;
        _displayGeometry.Reset();
        CameraName = null;
        _request = null;
        Header = Number.ToString();
        Status = TileStatus.Idle;
        StatusText = string.Empty;
        ErrorDetails = null;
        OnPropertyChanged(nameof(HasCamera));
    }

    public Task<bool> TakeSnapshotAsync(string filePath)
    {
        if (_isDisposed || Status != TileStatus.Playing) return Task.FromResult(false);
        var player = _player;
        var version = _streamVersion;
        return _operations.RunAsync(() =>
        {
            if (_isDisposed || version != _streamVersion) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            return player.TakeSnapshot(0, filePath, 0, 0);
        });
    }

    public bool CanAcceptDrop => _host.CanDropCameras;

    public void AcceptDrop(IReadOnlyList<CameraReference> cameras)
    {
        if (cameras.Count > 0 && CanAcceptDrop)
        {
            _host.DropCameras(this, cameras);
        }
    }

    public void Dispose()
    {
        _disposal ??= DisposeCoreAsync();
    }

    private async Task DisposeCoreAsync()
    {
        PlayRequestVersion++;
        _isDisposed = true;
        _reconnectTimer.Tick -= OnReconnectTick;
        _progressTimer.Tick -= OnProgressTick;
        _switchTimer.Tick -= OnSwitchTick;
        _hiddenTimer.Stop();
        _hiddenTimer.Tick -= OnHiddenTick;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        StopPlayer();
        // Finish native stops while the surfaces still exist, then detach on the UI thread.
        await Task.WhenAll(_openings);
        await _operations.WhenIdle;
        PrimaryPlayer = null;
        SecondaryPlayer = null;
        OnPropertyChanged(nameof(PrimaryPlayer));
        OnPropertyChanged(nameof(SecondaryPlayer));
        await _operations.RunAsync(() =>
        {
            _primaryPlayer.Dispose();
            _secondaryPlayer.Dispose();
        });
    }

    private void TrackOpening(Task task)
    {
        _openings.RemoveAll(opening => opening.IsCompleted);
        _openings.Add(task);
    }

    private void StopMedia(MediaPlayer player, VideoMedia? media) =>
        _ = _operations.RunAsync(() => VideoMedia.Stop(player, media));

    private void SetMute(MediaPlayer player, bool muted) =>
        _ = _operations.RunAsync(() => player.Mute = muted);

    [RelayCommand]
    private void Select()
    {
        _host.SelectTile(this);
    }

    [RelayCommand]
    private void ToggleMaximize()
    {
        _host.ToggleMaximize(this);
    }

    partial void OnIsAudioEnabledChanged(bool value)
    {
        if (!_isDisposed)
        {
            SetMute(_player, !value);
        }

        // Audio is left out of the media unless enabled, so the stream is reopened with or without it.
        if (_isDisposed || _request is null || _isStartPending)
        {
            return;
        }

        StopPlayer();
        _isStartPending = true;
        Status = TileStatus.Connecting;
        QueuePendingStart();
    }

    private void StartPlayback() => TrackOpening(StartPlaybackAsync());

    private async Task StartPlaybackAsync()
    {
        if (_request is null || _isDisposed || !_isViewReady) return;
        if (!IsVisibleInGrid) { _isStartPending = true; return; }
        _reconnectTimer.Stop();
        var request = _request;
        var player = _player;
        var version = ++_streamVersion;
        var withAudio = IsAudioEnabled;
        var hardware = !_softwareFallback;
        Status = TileStatus.Connecting;
        StatusText = string.Empty;
        ErrorDetails = null;
        _displayGeometry.Learn(request.AspectRatio);
        var ratio = _displayGeometry.AspectRatio;
        try
        {
            var media = await _operations.RunAsync(() => VideoMedia.Create(_libVlc, request.Source,
                request.Username, request.Password, request.NetworkCachingMs, withAudio));
            if (_isDisposed || version != _streamVersion)
            {
                await _operations.RunAsync(media.Dispose);
                return;
            }
            _media = media;
            _isAspectRatioReported = false;
            _lastDisplayedFrames = 0;
            _lastFrameAt = Stopwatch.GetTimestamp();
            var started = await _operations.RunAsync(() =>
            {
                if (_isDisposed || version != _streamVersion) return false;
                player.EnableHardwareDecoding = hardware;
                player.AspectRatio = ratio;
                player.Mute = !withAudio;
                return player.Play(media.Media);
            });
            if (_isDisposed || version != _streamVersion) return;
            if (!started) { HandleFailure(Localizer.Text("Tile_CannotPlay")); return; }
            _progressTimer.Start();
            OnPlayerPlaying(player);
        }
        catch (Exception exception)
        {
            if (!_isDisposed && version == _streamVersion) HandleFailure(exception.Message, exception);
        }
    }

    private void StopPlayer()
    {
        _streamVersion++;
        CancelSwitch();
        _reconnectTimer.Stop();
        _progressTimer.Stop();
        var media = _media;
        _media = null;
        StopMedia(_player, media);
    }

    private void HandleFailure(string message, Exception? exception = null)
    {
        if (_request is null || _isDisposed)
        {
            return;
        }

        Status = TileStatus.Error;
        _progressTimer.Stop();
        DeviceOperationErrors.Report($"Video playback: {Header}", exception ?? new DeviceCommunicationException(message));
        StatusText = Localizer.Format("Tile_Reconnecting", ReconnectDelay.TotalSeconds.ToString("0"));
        ErrorDetails = message;
        _reconnectTimer.Start();
    }

    private MediaPlayer CreatePlayer(LibVLC libVlc)
    {
        var player = new MediaPlayer(libVlc)
        {
            EnableHardwareDecoding = true
        };
        // LibVLCSharp does not pass the player as the event sender, so each player gets its own handlers.
        player.Playing += (_, _) => OnPlayerPlaying(player);
        player.EncounteredError += (_, _) => OnPlayerError(player);
        player.EndReached += (_, _) => OnPlayerEndReached(player);
        return player;
    }

    private MediaPlayer InactivePlayer => ReferenceEquals(_player, _primaryPlayer) ? _secondaryPlayer : _primaryPlayer;

    /// <summary>
    /// Opens the stream on the hidden player while the shown one keeps playing; the views swap once the
    /// new stream displays its first picture (see <see cref="OnSwitchTick"/>).
    /// </summary>
    private void BeginSwitch(TilePlayRequest request)
    {
        if (_switchRequest?.StreamType == request.StreamType) return;
        TrackOpening(BeginSwitchAsync(request));
    }

    private async Task BeginSwitchAsync(TilePlayRequest request)
    {
        PreserveDisplayGeometry();
        CancelSwitch();
        var version = _switchVersion;
        var player = InactivePlayer;
        if (!HasVideoSurface(player)) { ReportSwitchFailure(Localizer.Text("Tile_SwitchFailed")); return; }
        _switchRequest = request;
        var hardware = !_softwareFallback;
        var withAudio = IsAudioEnabled;
        _displayGeometry.Learn(request.AspectRatio);
        var ratio = _displayGeometry.AspectRatio;
        try
        {
            var media = await _operations.RunAsync(() => VideoMedia.Create(_libVlc, request.Source,
                request.Username, request.Password, request.NetworkCachingMs, withAudio));
            if (_isDisposed || version != _switchVersion)
            {
                await _operations.RunAsync(media.Dispose);
                return;
            }
            _switchMedia = media;
            var started = await _operations.RunAsync(() =>
            {
                if (_isDisposed || version != _switchVersion) return false;
                player.EnableHardwareDecoding = hardware;
                player.Mute = !withAudio;
                player.AspectRatio = ratio;
                return player.Play(media.Media);
            });
            if (_isDisposed || version != _switchVersion) return;
            if (!started) { CancelSwitch(); ReportSwitchFailure(Localizer.Text("Tile_SwitchFailed")); return; }
            _switchStartedAt = Stopwatch.GetTimestamp();
            _switchTimer.Start();
        }
        catch (Exception exception)
        {
            if (_isDisposed || version != _switchVersion) return;
            CancelSwitch();
            ReportSwitchFailure(exception.Message, exception);
        }
    }

    /// <summary>
    /// Tells the host the proportions of a playing main stream, once its picture size is known.
    /// </summary>
    private void ReportAspectRatio()
    {
        if (_media is null || _isAspectRatioReported || Camera is null || _request?.StreamType != StreamType.Main)
        {
            return;
        }

        var aspectRatio = ReadNaturalAspectRatio(_player, _media);
        if (aspectRatio is null)
        {
            return;
        }

        _isAspectRatioReported = true;
        _host.ReportMainAspectRatio(Camera, aspectRatio);
    }

    private void PreserveDisplayGeometry()
    {
        if (_media is null) return;
        if (_displayGeometry.AspectRatio is null)
            _displayGeometry.Learn(ReadNaturalAspectRatio(_player, _media));
        if (_displayGeometry.AspectRatio is not null && _player.AspectRatio != _displayGeometry.AspectRatio)
        {
            var player = _player;
            var ratio = _displayGeometry.AspectRatio;
            _ = _operations.RunAsync(() => player.AspectRatio = ratio);
        }
    }

    private static string? ReadNaturalAspectRatio(MediaPlayer player, VideoMedia? media)
    {
        if (media is not null)
        {
            foreach (var track in media.Media.Tracks)
            {
                if (track.TrackType != TrackType.Video) continue;
                var video = track.Data.Video;
                var ratio = VideoDisplayGeometry.FromDimensions(video.Width, video.Height, video.SarNum, video.SarDen);
                if (ratio is not null) return ratio;
            }
        }
        uint width = 0, height = 0;
        return player.Size(0, ref width, ref height) ? VideoDisplayGeometry.FromDimensions(width, height) : null;
    }

    private static bool HasVideoSurface(MediaPlayer player)
    {
        return player.NsObject != IntPtr.Zero || player.Hwnd != IntPtr.Zero || player.XWindow != 0;
    }

    private void OnSwitchTick(object? sender, EventArgs e)
    {
        if (_retiringPlayer is not null)
        {
            // Visibility changes map native windows asynchronously. Leave the old surface
            // playing underneath until the incoming one has presented another picture.
            var elapsed = Stopwatch.GetElapsedTime(_handoverStartedAt);
            if (_isDisposed || (elapsed >= SurfaceHandoverDelay && _media is not null
                    && _media.Media.Statistics.DisplayedPictures > _handoverDisplayedFrames))
            {
                ReleaseRetiringPlayer();
                _switchTimer.Stop();
            }
            else if (elapsed >= SurfaceHandoverTimeout)
            {
                RestoreRetiringPlayer();
            }
            return;
        }

        if (_switchMedia is null || _switchRequest is null || _isDisposed)
        {
            _switchTimer.Stop();
            return;
        }

        var player = InactivePlayer;
        if (player.State is VLCState.Error or VLCState.Ended || Stopwatch.GetElapsedTime(_switchStartedAt) > SwitchTimeout)
        {
            // A failed upgrade must not replace the working picture with a blank stream.
            var message = _switchMedia.ErrorMessage ?? Localizer.Text("Tile_SwitchFailed");
            CancelSwitch();
            ReportSwitchFailure(message);
            return;
        }

        var stats = _switchMedia.Media.Statistics;
        // Decoding or Playing alone does not mean the renderer has a picture to reveal.
        var isReady = player.State == VLCState.Playing && stats.DisplayedPictures >= 1;
        if (isReady && ReadNaturalAspectRatio(player, _switchMedia) is not null)
        {
            CompleteSwitch();
        }
    }

    private void ReportSwitchFailure(string message, Exception? exception = null)
    {
        ErrorDetails = message;
        DeviceOperationErrors.Report($"Switch video stream: {Header}", exception ?? new DeviceCommunicationException(message));
    }

    private void CompleteSwitch()
    {
        var oldPlayer = _player;
        var oldMedia = _media;
        var oldRequest = _request;

        _player = InactivePlayer;
        _media = _switchMedia;
        _request = _switchRequest;
        _switchMedia = null;
        _switchRequest = null;
        _retiringPlayer = oldPlayer;
        _retiringMedia = oldMedia;
        _retiringRequest = oldRequest;
        _handoverStartedAt = Stopwatch.GetTimestamp();
        _handoverDisplayedFrames = _media!.Media.Statistics.DisplayedPictures;

        SetMute(_player, !IsAudioEnabled);
        PreserveDisplayGeometry();
        _isAspectRatioReported = false;
        _lastDisplayedFrames = 0;
        _lastFrameAt = Stopwatch.GetTimestamp();
        ReportAspectRatio();
        OnPropertyChanged(nameof(IsPrimaryPlayerActive));
        // Always map the incoming surface first. Hiding the outgoing surface is a
        // separate timer step, after layout and native painting have had time to run.
        OnPropertyChanged(ReferenceEquals(_player, _primaryPlayer)
            ? nameof(IsPrimaryViewVisible) : nameof(IsSecondaryViewVisible));
        if (CameraName is not null)
        {
            Header = BuildHeader(CameraName);
        }
    }

    private void ReleaseRetiringPlayer()
    {
        var player = _retiringPlayer;
        var media = _retiringMedia;
        _retiringPlayer = null;
        _retiringMedia = null;
        _retiringRequest = null;
        if (player is null) return;
        OnPropertyChanged(ReferenceEquals(player, _primaryPlayer)
            ? nameof(IsPrimaryViewVisible) : nameof(IsSecondaryViewVisible));
        StopMedia(player, media);
    }

    private void RestoreRetiringPlayer(bool reportFailure = true)
    {
        var failedPlayer = _player;
        var failedMedia = _media;
        _player = _retiringPlayer!;
        _media = _retiringMedia;
        _request = _retiringRequest;
        _retiringPlayer = null;
        _retiringMedia = null;
        _retiringRequest = null;
        _switchTimer.Stop();
        _lastDisplayedFrames = 0;
        _lastFrameAt = Stopwatch.GetTimestamp();
        _isAspectRatioReported = false;
        SetMute(_player, !IsAudioEnabled);
        OnPropertyChanged(nameof(IsPrimaryPlayerActive));
        OnPropertyChanged(nameof(IsPrimaryViewVisible));
        OnPropertyChanged(nameof(IsSecondaryViewVisible));
        StopMedia(failedPlayer, failedMedia);
        if (CameraName is not null) Header = BuildHeader(CameraName);
        if (reportFailure) ReportSwitchFailure(Localizer.Text("Tile_SwitchFailed"));
    }

    private void CancelSwitch()
    {
        _switchVersion++;
        _switchTimer.Stop();
        ReleaseRetiringPlayer();
        if (_switchMedia is null)
        {
            _switchRequest = null;
            return;
        }

        StopMedia(InactivePlayer, _switchMedia);
        _switchMedia = null;
        _switchRequest = null;
    }

    private string BuildHeader(string cameraName)
    {
        var stream = _request?.StreamType == StreamType.Sub ? "SUB" : "MAIN";
        return $"{Number} · {cameraName} · {stream}";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(OverlayText));
    }

    private void OnReconnectTick(object? sender, EventArgs e)
    {
        _reconnectTimer.Stop();
        if (_request is null || _isDisposed)
        {
            return;
        }

        StopPlayer();
        StartPlayback();
    }

    private void OnProgressTick(object? sender, EventArgs e)
    {
        if (_media is null || _request is null || _isDisposed || !IsVisibleInGrid || !_isViewReady) return;
        PreserveDisplayGeometry();
        ReportAspectRatio();
        var stats = _media.Media.Statistics;
        if (stats.DisplayedPictures != _lastDisplayedFrames)
        {
            _lastDisplayedFrames = stats.DisplayedPictures;
            _lastFrameAt = Stopwatch.GetTimestamp();
            return;
        }
        if (Stopwatch.GetElapsedTime(_lastFrameAt) < TimeSpan.FromSeconds(15)) return;

        // VLC can remain in Playing with a stalled hardware decoder and never raise an error.
        UnhandledExceptionLogger.Log(
            $"Video stalled: {Header}; stream={_request.StreamType}; decoded={stats.DecodedVideo}; displayed={stats.DisplayedPictures}; lost={stats.LostPictures}; software={_softwareFallback}", null);
        StopPlayer();
        if (!_softwareFallback && stats.DecodedVideo > 0)
        {
            _softwareFallback = true;
            _isStartPending = true;
            QueuePendingStart();
        }
        else HandleFailure(Localizer.Text("Tile_Stalled"));
    }

    // LibVLC raises events on its own threads - marshal them to the UI thread.
    private void OnPlayerPlaying(MediaPlayer sender)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_media is null || !ReferenceEquals(sender, _player) || _request is null || _isDisposed || _isStartPending
                || !_isViewReady || _player.State != VLCState.Playing)
            {
                return;
            }

            SetMute(_player, !IsAudioEnabled);
            PreserveDisplayGeometry();
            Status = TileStatus.Playing;
            ReportAspectRatio();
            StatusText = string.Empty;
            ErrorDetails = null;
        });
    }

    private void OnPlayerError(MediaPlayer sender)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _player) && _media is not null && !_isStartPending && _isViewReady && _player.State == VLCState.Error)
                HandleFailure(_media.ErrorMessage ?? Localizer.Text("Tile_CannotPlay"));
        });
    }

    private void OnPlayerEndReached(MediaPlayer sender)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _player) && _media is not null && !_isStartPending && _isViewReady && _player.State == VLCState.Ended)
                HandleFailure(_media.ErrorMessage ?? Localizer.Text("Tile_Interrupted"));
        });
    }
}
