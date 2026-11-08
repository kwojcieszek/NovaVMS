using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Preview tile of one camera in a camera group; tapping it opens the camera.</summary>
public sealed partial class CameraTileViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly MobileCamera _camera;
    private readonly CameraCollection _collection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMobilePlayer? _player;
    private IDeviceClient? _client;
    private bool _released;

    [ObservableProperty]
    private Control? _video;

    [ObservableProperty]
    private string _state;

    public string Title => _camera.Title;

    public string Subtitle => _camera.Subtitle;

    public bool IsFavorite => _camera.Channel.IsFavorite;

    public string AutomationName => $"{(IsFavorite ? "★ " : "")}{Title}\n{Subtitle}";

    public CameraTileViewModel(ShellViewModel shell, MobileCamera camera, CameraCollection collection)
    {
        _shell = shell;
        _camera = camera;
        _collection = collection;
        _state = Localizer.Text("Mobile_Connecting");
    }

    /// <summary>
    /// Connects and plays the sub stream with the preview limits; failures are shown on the tile.
    /// <paramref name="starts"/> limits how many previews connect at the same time.
    /// </summary>
    public async Task StartAsync(PlaybackOptions options, SemaphoreSlim starts, CancellationToken token)
    {
        await _gate.WaitAsync(CancellationToken.None);

        var startSlot = false;

        try
        {
            if (_released || token.IsCancellationRequested)
            {
                return;
            }

            await starts.WaitAsync(token);

            startSlot = true;
            State = Localizer.Text("Mobile_Connecting");

            _player = _shell.CreatePreviewPlayer();
            _player.SetFillArea(_shell.Current.Settings.VideoAspectMode == VideoAspectMode.Fit);
            Video = _player.View;
            _client = await MobileDevices.ConnectAsync(_camera.Device, _shell.LocalSettings.UseOptionalProtocol, token);

            var source = await _client.GetLiveSourceAsync(_camera.Channel.Number, StreamType.Sub, token);

            await _player.PlayAsync(source, _camera.Device, options, token);

            _player.SetMuted(true);
            State = string.Empty;
        }
        catch (OperationCanceledException)
        {
            State = string.Empty;
        }
        catch
        {
            State = Localizer.Text("Mobile_PreviewFailed");
        }
        finally
        {
            if (startSlot)
            {
                starts.Release();
            }

            _gate.Release();
        }
    }

    /// <summary>Stops the preview of a tile that leaves the screen; it can be started again.</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync();

        try
        {
            await StopPlayerAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the preview for good; called when the camera group is left.</summary>
    public async Task ReleaseAsync()
    {
        await _gate.WaitAsync();

        try
        {
            _released = true;

            await StopPlayerAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    [RelayCommand]
    private Task OpenAsync()
    {
        return _shell.GuardAsync(() => _shell.OpenCameraAsync(_camera.Device, _camera.Channel, _collection));
    }

    /// <summary>
    /// Stops the preview before its native surface is removed, then releases the player.
    /// A LibVLC video output whose surface disappears while it plays is never shut down.
    /// </summary>
    private async Task StopPlayerAsync()
    {
        try
        {
            if (_player is { } player)
            {
                _player = null;

                await player.StopAsync();

                Video = null;

                await player.DisposeAsync();
            }

            Video = null;

            _client?.Dispose();
            _client = null;
        }
        catch
        {
            // The platform may already have destroyed the native surface.
        }
    }
}
