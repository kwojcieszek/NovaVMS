using Avalonia.Controls;
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
    private readonly ShellViewModel _shell;
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

    public override MobileTab Tab => MobileTab.Cameras;

    /// <summary>A camera opened from the live previews fills the landscape screen without controls.</summary>
    public bool IsFullScreen => _returnTo is not null;

    public bool IsWindowed => !IsFullScreen;

    public string Title => _channel.DisplayName;

    public string Subtitle => _device.DisplayName;

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
        Video = null;
    }

    internal async Task PlayLiveAsync(CancellationToken token)
    {
        if (!_shell.IsCurrentClient(_client))
        {
            return;
        }

        var streamType = _shell.MainStream ? StreamType.Main : StreamType.Sub;
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

    partial void OnSoundEnabledChanged(bool value)
    {
        _shell.Muted = !value;
        _shell.Player.SetMuted(_shell.Muted);
    }
}
