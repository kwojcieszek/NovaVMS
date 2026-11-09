using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Live view of one camera with playback controls, PTZ and recordings.</summary>
public sealed partial class CameraViewModel : PageViewModel
{
    private static readonly TimeSpan OverlayHideDelay = TimeSpan.FromSeconds(5);

    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _overlayTimer;
    private readonly IDeviceClient _client;
    private readonly Device _device;
    private readonly Channel _channel;
    private readonly CameraCollection? _returnTo;
    private bool _paused;

    [ObservableProperty]
    private Control? _video;

    [ObservableProperty]
    private bool _mainStream;

    [ObservableProperty]
    private bool _soundEnabled;

    /// <summary>The transparent playback menu above a full screen camera; a tap on the picture shows it.</summary>
    [ObservableProperty]
    private bool _isOverlayVisible;

    /// <summary>Recordings are played instead of the live picture.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    private bool _isPlayback;

    /// <summary>What is shown: live picture or recordings from a time; shown in the playback menu.</summary>
    [ObservableProperty]
    private string _modeText = string.Empty;

    public override MobileTab Tab => MobileTab.Cameras;

    /// <summary>A camera opened from the live previews fills the landscape screen without controls.</summary>
    public bool IsFullScreen => _returnTo is not null;

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
        _video = shell.Player.View;
        _mainStream = shell.MainStream;
        _soundEnabled = !shell.Muted;

        _modeText = Localizer.Text("Mobile_Live");
        _overlayTimer = new DispatcherTimer { Interval = OverlayHideDelay };
        _overlayTimer.Tick += (_, _) => IsOverlayVisible = false;

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

    internal async Task PlayLiveAsync(CancellationToken token)
    {
        if (!_shell.IsCurrentClient(_client))
        {
            return;
        }

        // A camera opened full screen from the previews fills the screen, so it always gets the full quality stream.
        var streamType = IsFullScreen || _shell.MainStream ? StreamType.Main : StreamType.Sub;
        var source = await _client.GetLiveSourceAsync(_channel.Number, streamType, token);

        token.ThrowIfCancellationRequested();

        await StartAsync(source, streamType, token);
    }

    internal async Task PlayRecordingAsync(RecordingSegment recording, CancellationToken token)
    {
        if (!_shell.IsCurrentClient(_client))
        {
            return;
        }

        var source = await _client.GetPlaybackSourceAsync(_channel.Number, recording.Start, recording.End, token);

        await StartAsync(source, StreamType.Main, token);
    }

    private async Task StartAsync(VideoSource source, StreamType streamType, CancellationToken token)
    {
        var options = PlaybackPolicy.ForCamera(_shell.Current.Settings, streamType);

        await _shell.Player.PlayAsync(source, _device, options, token);

        _shell.Player.SetMuted(_shell.Muted);
        _paused = false;
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

        return _shell.RunAsync(async token =>
        {
            IsPlayback = false;
            ModeText = Localizer.Text("Mobile_Live");

            await PlayLiveAsync(token);
        });
    }

    /// <summary>Plays the recordings of today from the first one, in the recorder's local time.</summary>
    [RelayCommand]
    private Task ShowRecordingsAsync()
    {
        KeepOverlay();

        return _shell.RunAsync(async token =>
        {
            if (!_shell.IsCurrentClient(_client))
            {
                return;
            }

            var day = DateTime.Today;
            var now = DateTime.Now;
            var recordings = await _client.SearchRecordingsAsync(_channel.Number, day, day.AddDays(1), token);
            var first = recordings.Where(recording => recording.End > day && recording.Start < now).MinBy(recording => recording.Start);

            token.ThrowIfCancellationRequested();

            if (first is null)
            {
                // The status bar is hidden full screen, so the menu says it too.
                _shell.Status = ModeText = Localizer.Text("Mobile_NoRecordingsToday");
                KeepOverlay();

                return;
            }

            var start = first.Start < day ? day : first.Start;
            var source = await _client.GetPlaybackSourceAsync(_channel.Number, start, now, token);

            token.ThrowIfCancellationRequested();

            await StartAsync(source, StreamType.Main, token);

            IsPlayback = true;
            ModeText = Localizer.Format("Mobile_RecordingsFrom", start.ToString("t", Localizer.Instance.Culture));
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
    }

    [RelayCommand]
    private Task StopStreamAsync()
    {
        return _shell.RunAsync(async _ =>
        {
            await _shell.Player.StopAsync();

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
        }

        _shell.Player.SetOverlayVisible(value);
    }

    partial void OnSoundEnabledChanged(bool value)
    {
        _shell.Muted = !value;
        _shell.Player.SetMuted(_shell.Muted);
    }
}
