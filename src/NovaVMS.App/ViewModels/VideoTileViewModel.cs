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

    private readonly LibVLC _libVlc;
    private readonly ITileHost _host;
    private readonly MediaPlayer _player;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly DispatcherTimer _progressTimer;
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

        _player = new MediaPlayer(libVlc)
        {
            EnableHardwareDecoding = true
        };
        _player.Playing += OnPlayerPlaying;
        _player.EncounteredError += OnPlayerError;
        _player.EndReached += OnPlayerEndReached;
        VideoPlayer = _player;

        _reconnectTimer = new DispatcherTimer
        {
            Interval = ReconnectDelay
        };
        _reconnectTimer.Tick += OnReconnectTick;
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _progressTimer.Tick += OnProgressTick;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        _header = Number.ToString();
    }

    public int Index { get; }

    public int Number => Index + 1;

    /// <summary>
    /// Player shown by the video view. Cleared before the player is disposed, so the view
    /// detaches from a live native player instead of a released one.
    /// </summary>
    public MediaPlayer? VideoPlayer { get; private set; }

    public CameraReference? Camera { get; private set; }

    public string? CameraName { get; private set; }

    public bool HasCamera => Camera is not null;

    public bool HasError => Status == TileStatus.Error;

    public StreamType? ActiveStreamType => _request?.StreamType;

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
        StopPlayer();

        if (!Equals(Camera, camera))
        {
            _softwareFallback = false;
            _player.EnableHardwareDecoding = true;
        }
        Camera = camera;
        CameraName = cameraName;
        _request = request;
        Header = $"{Number} · {cameraName}";
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
        if (_isDisposed) return;
        if (!value)
        {
            StopPlayer();
            _isStartPending = _request is not null;
        }
        else QueuePendingStart();
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
        Header = $"{Number} · {cameraName}";
    }

    public void Close()
    {
        _isStartPending = false;
        StopPlayer();

        Camera = null;
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
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        StopPlayer();

        // Detach the video view first; disposing a player still attached to a view crashes the runtime.
        VideoPlayer = null;
        OnPropertyChanged(nameof(VideoPlayer));

        _player.Playing -= OnPlayerPlaying;
        _player.EncounteredError -= OnPlayerError;
        _player.EndReached -= OnPlayerEndReached;
        _player.Dispose();
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
    }

    private void StartPlayback()
    {
        if (_request is null || _isDisposed || !_isViewReady || !IsVisibleInGrid)
        {
            return;
        }

        _reconnectTimer.Stop();
        _media?.Dispose();
        try
        {
            _media = VideoMedia.Create(_libVlc, _request.Source, _request.Username, _request.Password, _request.NetworkCachingMs);
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            HandleFailure(exception.Message, exception);
            return;
        }

        Status = TileStatus.Connecting;
        StatusText = string.Empty;
        ErrorDetails = null;

        _lastDisplayedFrames = 0;
        _lastFrameAt = Stopwatch.GetTimestamp();
        _player.Play(_media.Media);
        _progressTimer.Start();
    }

    private void StopPlayer()
    {
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
    private void OnPlayerPlaying(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_request is null || _isDisposed || _isStartPending || !IsVisibleInGrid
                || !_isViewReady || _player.State != VLCState.Playing)
            {
                return;
            }

            _player.Mute = !IsAudioEnabled;
            Status = TileStatus.Playing;
            StatusText = string.Empty;
            ErrorDetails = null;
        });
    }

    private void OnPlayerError(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_media is not null && !_isStartPending && IsVisibleInGrid && _isViewReady && _player.State == VLCState.Error)
                HandleFailure(_media.ErrorMessage ?? Localizer.Text("Tile_CannotPlay"));
        });
    }

    private void OnPlayerEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_media is not null && !_isStartPending && IsVisibleInGrid && _isViewReady && _player.State == VLCState.Ended)
                HandleFailure(_media.ErrorMessage ?? Localizer.Text("Tile_Interrupted"));
        });
    }
}
