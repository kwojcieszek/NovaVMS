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

    private readonly LibVLC _libVlc;
    private readonly ITileHost _host;
    private readonly MediaPlayer _primaryPlayer;
    private readonly MediaPlayer _secondaryPlayer;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _switchTimer;
    // Player shown in the tile; the other one opens the next stream during a seamless switch.
    private MediaPlayer _player;
    private VideoMedia? _switchMedia;
    private TilePlayRequest? _switchRequest;
    private long _switchStartedAt;
    private long _switchFirstDecodedAt;
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
        _switchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _switchTimer.Tick += OnSwitchTick;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        _header = Number.ToString();
    }

    public int Index { get; }

    public int Number => Index + 1;

    /// <summary>
    /// Players shown by the two video views of the tile. Cleared before the players are disposed, so the
    /// views detach from live native players instead of released ones.
    /// </summary>
    public MediaPlayer? PrimaryPlayer { get; private set; }

    public MediaPlayer? SecondaryPlayer { get; private set; }

    public bool IsPrimaryViewVisible => IsVisibleInGrid && ReferenceEquals(_player, _primaryPlayer);

    public bool IsSecondaryViewVisible => IsVisibleInGrid && ReferenceEquals(_player, _secondaryPlayer);

    public CameraReference? Camera { get; private set; }

    public string? CameraName { get; private set; }

    public bool HasCamera => Camera is not null;

    public bool HasError => Status == TileStatus.Error;

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
                    return Localizer.Text("Tile_Connecting");
                }
                case TileStatus.Error:
                {
                    return $"{ErrorDetails}\n{StatusText}";
                }
                case TileStatus.Idle:
                {
                    return Localizer.Text("Tile_DragHere");
                }
                default:
                {
                    return string.Empty;
                }
            }
        }
    }

    public bool IsOverlayTextVisible => Status != TileStatus.Playing;

    public void Play(CameraReference camera, string cameraName, TilePlayRequest request)
    {
        var isPlayingCamera = Equals(Camera, camera) && Status == TileStatus.Playing && _isViewReady && IsVisibleInGrid
            && _request is not null && !_isStartPending;
        if (isPlayingCamera && _request!.StreamType == request.StreamType)
        {
            // Back to the stream that is still shown (e.g. restored before the switch completed).
            CancelSwitch();
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
        _player.EnableHardwareDecoding = !_softwareFallback;
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
        // A tile hidden behind a maximized one keeps playing, so restoring the split is instant
        // and maximizing does not wait for every other player to stop.
        if (value)
        {
            _lastFrameAt = Stopwatch.GetTimestamp();
            QueuePendingStart();
        }
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
        _isStartPending = false;
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

    public bool TakeSnapshot(string filePath)
    {
        return Status == TileStatus.Playing && _player.TakeSnapshot(0, filePath, 0, 0);
    }

    public void AcceptDrop(IReadOnlyList<CameraReference> cameras)
    {
        if (cameras.Count > 0)
        {
            _host.DropCameras(this, cameras);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _reconnectTimer.Tick -= OnReconnectTick;
        _progressTimer.Tick -= OnProgressTick;
        _switchTimer.Tick -= OnSwitchTick;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        StopPlayer();

        // Detach the video views first; disposing a player still attached to a view crashes the runtime.
        PrimaryPlayer = null;
        SecondaryPlayer = null;
        OnPropertyChanged(nameof(PrimaryPlayer));
        OnPropertyChanged(nameof(SecondaryPlayer));

        _primaryPlayer.Dispose();
        _secondaryPlayer.Dispose();
    }

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
            _player.Mute = !value;
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

    private void StartPlayback()
    {
        if (_request is null || _isDisposed || !_isViewReady)
        {
            return;
        }

        if (!IsVisibleInGrid)
        {
            // Started when the tile is shown again.
            _isStartPending = true;
            return;
        }

        _reconnectTimer.Stop();
        _media?.Dispose();
        try
        {
            _media = VideoMedia.Create(_libVlc, _request.Source, _request.Username, _request.Password, _request.NetworkCachingMs, IsAudioEnabled);
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            HandleFailure(exception.Message, exception);
            return;
        }

        Status = TileStatus.Connecting;
        StatusText = string.Empty;
        ErrorDetails = null;

        _displayGeometry.Learn(_request.AspectRatio);
        _player.AspectRatio = _displayGeometry.AspectRatio;
        _isAspectRatioReported = false;
        _lastDisplayedFrames = 0;
        _lastFrameAt = Stopwatch.GetTimestamp();
        _player.Play(_media.Media);
        _progressTimer.Start();
    }

    private void StopPlayer()
    {
        CancelSwitch();
        _reconnectTimer.Stop();
        _progressTimer.Stop();
        if (_isDisposed && _media is null)
        {
            return;
        }

        _media?.StopTransfer();
        _player.Stop();
        _media?.Dispose();
        _media = null;
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
        // Capture the shown picture before opening another resolution or decoder.
        // In particular, a first SUB -> MAIN switch may have no saved main ratio yet.
        PreserveDisplayGeometry();
        CancelSwitch();

        var player = InactivePlayer;
        if (!HasVideoSurface(player))
        {
            // Without its view's native surface VLC would open the stream in a window of its own.
            RestartWith(request);
            return;
        }

        player.EnableHardwareDecoding = !_softwareFallback;
        try
        {
            _switchMedia = VideoMedia.Create(_libVlc, request.Source, request.Username, request.Password, request.NetworkCachingMs, IsAudioEnabled);
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            RestartWith(request);
            return;
        }

        _switchRequest = request;
        _switchStartedAt = Stopwatch.GetTimestamp();
        _switchFirstDecodedAt = 0;
        player.Mute = !IsAudioEnabled;
        _displayGeometry.Learn(request.AspectRatio);
        player.AspectRatio = _displayGeometry.AspectRatio;
        player.Play(_switchMedia.Media);
        _switchTimer.Start();
    }

    /// <summary>
    /// Tells the host the proportions of a playing main stream, once its picture size is known.
    /// </summary>
    private void ReportAspectRatio()
    {
        if (_isAspectRatioReported || Camera is null || _request?.StreamType != StreamType.Main)
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
        if (_displayGeometry.AspectRatio is null)
            _displayGeometry.Learn(ReadNaturalAspectRatio(_player, _media));
        if (_displayGeometry.AspectRatio is not null && _player.AspectRatio != _displayGeometry.AspectRatio)
            _player.AspectRatio = _displayGeometry.AspectRatio;
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
        if (_switchMedia is null || _switchRequest is null || _isDisposed)
        {
            _switchTimer.Stop();
            return;
        }

        var player = InactivePlayer;
        if (player.State is VLCState.Error or VLCState.Ended || Stopwatch.GetElapsedTime(_switchStartedAt) > SwitchTimeout)
        {
            // The new stream failed: open it the usual way, which reports the error and reconnects.
            RestartWith(_switchRequest);
            return;
        }

        var stats = _switchMedia.Media.Statistics;
        if (stats.DecodedVideo > 0 && _switchFirstDecodedAt == 0)
        {
            _switchFirstDecodedAt = Stopwatch.GetTimestamp();
        }

        // A hidden view may not count displayed pictures, so a decoded picture held for the buffer time also counts.
        var isReady = stats.DisplayedPictures > 0
            || (player.State == VLCState.Playing && _switchFirstDecodedAt != 0
                && Stopwatch.GetElapsedTime(_switchFirstDecodedAt).TotalMilliseconds >= _switchRequest.NetworkCachingMs);
        if (isReady && ReadNaturalAspectRatio(player, _switchMedia) is not null)
        {
            CompleteSwitch();
        }
    }

    private void CompleteSwitch()
    {
        _switchTimer.Stop();
        var oldPlayer = _player;
        var oldMedia = _media;

        _player = InactivePlayer;
        _media = _switchMedia;
        _request = _switchRequest;
        _switchMedia = null;
        _switchRequest = null;

        _player.Mute = !IsAudioEnabled;
        PreserveDisplayGeometry();
        _isAspectRatioReported = false;
        _lastDisplayedFrames = 0;
        _lastFrameAt = Stopwatch.GetTimestamp();
        ReportAspectRatio();
        OnPropertyChanged(nameof(IsPrimaryViewVisible));
        OnPropertyChanged(nameof(IsSecondaryViewVisible));
        if (CameraName is not null)
        {
            Header = BuildHeader(CameraName);
        }

        oldMedia?.StopTransfer();
        oldPlayer.Stop();
        oldMedia?.Dispose();
    }

    private void CancelSwitch()
    {
        _switchTimer.Stop();
        if (_switchMedia is null)
        {
            _switchRequest = null;
            return;
        }

        _switchMedia.StopTransfer();
        InactivePlayer.Stop();
        _switchMedia.Dispose();
        _switchMedia = null;
        _switchRequest = null;
    }

    private void RestartWith(TilePlayRequest request)
    {
        StopPlayer();
        _request = request;
        if (CameraName is not null)
        {
            Header = BuildHeader(CameraName);
        }

        _isStartPending = true;
        Status = TileStatus.Connecting;
        QueuePendingStart();
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

        _media?.StopTransfer();
        _player.Stop();
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
            _player.EnableHardwareDecoding = false;
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
            if (!ReferenceEquals(sender, _player) || _request is null || _isDisposed || _isStartPending
                || !_isViewReady || _player.State != VLCState.Playing)
            {
                return;
            }

            _player.Mute = !IsAudioEnabled;
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
