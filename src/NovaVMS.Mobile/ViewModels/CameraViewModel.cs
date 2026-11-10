using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Live view of one camera with playback controls, PTZ and recordings.</summary>
public sealed partial class CameraViewModel : PageViewModel
{
    private static readonly TimeSpan OverlayHideDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PositionInterval = TimeSpan.FromSeconds(1);

    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _overlayTimer;
    private readonly DispatcherTimer _positionTimer;
    private readonly Stopwatch _playbackClock = new();
    private readonly IDeviceClient _client;
    private readonly Device _device;
    private readonly Channel _channel;
    private readonly CameraCollection? _returnTo;
    private DateTime _playbackStart;
    private bool _paused;

    [ObservableProperty]
    private Control? _video;

    [ObservableProperty]
    private bool _mainStream;

    [ObservableProperty]
    private bool _soundEnabled;

    /// <summary>
    /// The picture fills the landscape screen without controls. A camera opened from the live previews starts so;
    /// any camera enters and leaves it with a double tap on the picture.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowed))]
    private bool _isFullScreen;

    /// <summary>The transparent playback menu above a full screen camera; a tap on the picture shows it.</summary>
    [ObservableProperty]
    private bool _isOverlayVisible;

    /// <summary>Recordings are played instead of the live picture.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    private bool _isPlayback;

    /// <summary>What is shown: live picture or the recorded time being played; shown in the playback menu.</summary>
    [ObservableProperty]
    private string _modeText = string.Empty;

    public override MobileTab Tab => MobileTab.Cameras;

    public bool IsWindowed => !IsFullScreen;

    /// <summary>The sound switch is shown only when the player of this phone plays sound.</summary>
    public bool CanPlaySound => _shell.Player.SupportsAudio;

    public string Title => _channel.DisplayName;

    public string Subtitle => _device.DisplayName;

    public bool IsLive => !IsPlayback;

    /// <summary>The playback menu keeps clear of the notch and the rounded corners of the landscape screen.</summary>
    public Thickness OverlayInsets => _shell.SafeArea;

    public PtzViewModel Ptz { get; }

    public RecordingsViewModel Recordings { get; }

    public CameraViewModel(ShellViewModel shell, IDeviceClient client, Device device, Channel channel, CameraCollection? returnTo)
    {
        _shell = shell;
        _client = client;
        _device = device;
        _channel = channel;
        _returnTo = returnTo;
        _isFullScreen = returnTo is not null;
        _video = shell.Player.View;
        _mainStream = shell.MainStream;
        _soundEnabled = !shell.Muted;

        _modeText = Localizer.Text("Mobile_Live");
        _overlayTimer = new DispatcherTimer { Interval = OverlayHideDelay };
        _overlayTimer.Tick += (_, _) => HideOverlayWhenIdle();
        _positionTimer = new DispatcherTimer { Interval = PositionInterval };
        _positionTimer.Tick += (_, _) => UpdatePosition();

        Ptz = new PtzViewModel(shell, client, channel);
        Recordings = new RecordingsViewModel(shell, this, client, channel);
    }

    /// <summary>Returns to the live previews of the group the camera was opened from.</summary>
    public override void NavigateBack(ShellViewModel shell)
    {
        if (_returnTo is null)
        {
            shell.ShowCameras();
        }
        else
        {
            shell.ShowCameraCollection(_returnTo);
        }
    }

    /// <summary>Releases the native video view so another page can host it.</summary>
    public void Detach()
    {
        _overlayTimer.Stop();
        _positionTimer.Stop();
        _shell.Player.SetOverlay(null);

        Video = null;
    }

    /// <summary>Places the playback menu above the picture of a full screen camera.</summary>
    public void AttachOverlay(Control overlay)
    {
        if (IsFullScreen)
        {
            _shell.Player.SetOverlay(overlay);
        }
    }

    /// <summary>A single tap on the picture shows the menu; a tap on the menu background hides it again.</summary>
    public void ToggleOverlay()
    {
        IsOverlayVisible = !IsOverlayVisible;
    }

    /// <summary>Keeps the menu open while it is used.</summary>
    public void KeepOverlay()
    {
        if (IsOverlayVisible)
        {
            _overlayTimer.Stop();
            _overlayTimer.Start();
        }
    }

    /// <summary>
    /// A double tap on the picture: a windowed camera fills the screen, a full screen camera opened from the previews
    /// returns to them and any other full screen camera shows its page again.
    /// </summary>
    public void ToggleFullScreen()
    {
        if (!IsFullScreen)
        {
            IsFullScreen = true;

            return;
        }

        if (_returnTo is not null)
        {
            if (BackCommand.CanExecute(null))
            {
                BackCommand.Execute(null);
            }

            return;
        }

        IsFullScreen = false;
    }

    /// <summary>Reports a message; the status bar is hidden full screen, so the menu says it too.</summary>
    internal void ShowMessage(string message)
    {
        _shell.Status = message;

        if (IsFullScreen)
        {
            ModeText = message;

            KeepOverlay();
        }
    }

    internal async Task PlayLiveAsync(CancellationToken token)
    {
        if (!_shell.IsCurrentClient(_client))
        {
            return;
        }

        // A full screen camera fills the screen, so it always gets the full quality stream.
        var streamType = IsFullScreen || _shell.MainStream ? StreamType.Main : StreamType.Sub;
        var source = await _client.GetLiveSourceAsync(_channel.Number, streamType, token);

        token.ThrowIfCancellationRequested();

        await StartAsync(source, streamType, token);

        StopPositionClock();

        IsPlayback = false;
        ModeText = Localizer.Text("Mobile_Live");
    }

    /// <summary>Plays the recordings from <paramref name="start"/> until the end of its day, in the recorder's local time.</summary>
    internal async Task PlayFromAsync(DateTime start, CancellationToken token)
    {
        if (!_shell.IsCurrentClient(_client))
        {
            return;
        }

        var now = DateTime.Now;
        var dayEnd = start.Date.AddDays(1);
        var end = dayEnd < now ? dayEnd : now;

        if (end <= start)
        {
            ShowMessage(Localizer.Text("Mobile_NoRecordingsAfter"));

            return;
        }

        var source = await _client.GetPlaybackSourceAsync(_channel.Number, start, end, token);

        token.ThrowIfCancellationRequested();

        await StartAsync(source, StreamType.Main, token);

        IsPlayback = true;
        _playbackStart = start;

        _playbackClock.Restart();
        _positionTimer.Start();
        UpdatePosition();
    }

    private async Task StartAsync(VideoSource source, StreamType streamType, CancellationToken token)
    {
        var options = PlaybackPolicy.ForCamera(_shell.Current.Settings, streamType);

        await _shell.Player.PlayAsync(source, _device, options, token);

        _shell.Player.SetMuted(_shell.Muted);
        _paused = false;
    }

    // The menu stays while the calendar is open: choosing a day takes longer than the hide delay.
    private void HideOverlayWhenIdle()
    {
        if (!Recordings.IsCalendarOpen)
        {
            IsOverlayVisible = false;
        }
    }

    // The recorder sends the recording at its own speed, so the time played follows the clock of the phone.
    private void UpdatePosition()
    {
        if (!IsPlayback)
        {
            return;
        }

        var position = _playbackStart + _playbackClock.Elapsed;

        Recordings.Position = position;
        ModeText = Localizer.Format("Mobile_RecordingAt", position.ToString("T", Localizer.Instance.Culture));
    }

    private void StopPositionClock()
    {
        _positionTimer.Stop();
        _playbackClock.Reset();

        Recordings.Position = null;
    }

    [RelayCommand]
    private Task BackAsync()
    {
        return _shell.GuardAsync(async () =>
        {
            await _shell.CloseCameraAsync();
            NavigateBack(_shell);
        });
    }

    [RelayCommand]
    private Task ShowLiveAsync()
    {
        KeepOverlay();

        if (!IsPlayback)
        {
            return Task.CompletedTask;
        }

        return _shell.RunAsync(PlayLiveAsync);
    }

    /// <summary>Plays the recordings of the chosen day from the first one, in the recorder's local time.</summary>
    [RelayCommand]
    private Task ShowRecordingsAsync()
    {
        KeepOverlay();

        return _shell.RunAsync(async token =>
        {
            await Recordings.LoadAsync(token);

            if (Recordings.FindRecordedTime(Recordings.Day) is not { } start)
            {
                ShowMessage(Localizer.Text("Mobile_NoRecordings"));

                return;
            }

            await PlayFromAsync(start, token);
        });
    }

    [RelayCommand]
    private Task ReconnectAsync()
    {
        return _shell.RunAsync(PlayLiveAsync);
    }

    [RelayCommand]
    private void TogglePause()
    {
        _paused = !_paused;

        _shell.Player.SetPaused(_paused);

        // A paused recording continues from a later moment, as the recorder keeps sending it.
        if (_paused)
        {
            _positionTimer.Stop();
        }
        else if (IsPlayback)
        {
            _positionTimer.Start();
        }
    }

    [RelayCommand]
    private Task StopStreamAsync()
    {
        return _shell.RunAsync(async _ =>
        {
            await _shell.Player.StopAsync();

            StopPositionClock();

            _shell.Status = Localizer.Text("Mobile_Stopped");
        });
    }

    [RelayCommand]
    private Task ToggleFavoriteAsync()
    {
        return _shell.RunAsync(async token =>
        {
            _channel.IsFavorite = !_channel.IsFavorite;

            try
            {
                await _shell.Configuration.SaveAsync(token);
            }
            catch
            {
                _channel.IsFavorite = !_channel.IsFavorite;

                throw;
            }

            _shell.Status = Localizer.Text(_channel.IsFavorite ? "Mobile_FavoriteAdded" : "Mobile_FavoriteRemoved");
        });
    }

    async partial void OnMainStreamChanged(bool value)
    {
        _shell.MainStream = value;

        await _shell.RunAsync(PlayLiveAsync);
    }

    partial void OnIsFullScreenChanged(bool value)
    {
        if (!value)
        {
            IsOverlayVisible = false;

            _shell.Player.SetOverlay(null);
        }

        _shell.IsFullScreen = value;
    }

    partial void OnIsOverlayVisibleChanged(bool value)
    {
        if (value)
        {
            OnPropertyChanged(nameof(OverlayInsets));
            _overlayTimer.Start();
        }
        else
        {
            _overlayTimer.Stop();

            Recordings.IsCalendarOpen = false;
        }

        _shell.Player.SetOverlayVisible(value);
    }

    partial void OnSoundEnabledChanged(bool value)
    {
        _shell.Muted = !value;
        _shell.Player.SetMuted(_shell.Muted);
    }
}
