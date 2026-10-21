using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using NovaVMS.App.Localization;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

public sealed partial class GroupCameraPreviewViewModel : ViewModelBase, ITileHost, IDisposable
{
    private readonly IDeviceClientFactory _factory;
    private readonly ConfigurationService _configuration;
    private CancellationTokenSource? _opening;
    private CameraDescriptor? _camera;
    private bool _active;
    private bool _disposed;
    private int _version;
    [ObservableProperty] private string _message = string.Empty;
    public VideoTileViewModel Tile { get; }
    public Task Disposal => Tile.Disposal;

    public GroupCameraPreviewViewModel(LibVLC vlc, IDeviceClientFactory factory, ConfigurationService configuration)
    {
        _factory = factory;
        _configuration = configuration;
        Tile = new VideoTileViewModel(vlc, 0, this);
    }

    public void Select(CameraDescriptor? camera)
    {
        // The same camera is opened again only when its previous attempt failed or was closed.
        if (_disposed || (Equals(camera?.Reference, _camera?.Reference) && Tile.Status is TileStatus.Playing or TileStatus.Connecting)) return;
        _camera = camera;
        Restart();
    }

    public void SetActive(bool active)
    {
        if (_disposed || _active == active) return;
        _active = active;
        Restart();
    }

    private void Restart()
    {
        var version = ++_version;
        _opening?.Cancel();
        _opening?.Dispose();
        _opening = null;
        Tile.Close();
        Message = string.Empty;
        if (!_active || _camera is null) return;
        Tile.ShowLoading();
        _opening = new CancellationTokenSource();
        _ = OpenAsync(_camera, version, _opening.Token);
    }

    private async Task OpenAsync(CameraDescriptor camera, int version, CancellationToken token)
    {
        try
        {
            var device = camera.Device.ForHost(camera.Device.Host);
            var source = await LiveSourceLoader.OpenAsync(_factory, device, camera.Channel.Number, StreamType.Sub, token);
            if (_disposed || version != _version || token.IsCancellationRequested) return;
            Tile.Play(camera.Reference, camera.FullName, new TilePlayRequest(source, StreamType.Sub,
                device.Username, device.Password, _configuration.Current.Settings.NetworkCachingMs, camera.Channel.MainAspectRatio,
                _configuration.Current.Settings.GetMaxFrameRate(StreamType.Sub)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed && version == _version)
                Message = DeviceOperationErrors.Report("Group camera preview", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _version++;
        _opening?.Cancel();
        _opening?.Dispose();
        Tile.Dispose();
    }
    public void SelectTile(VideoTileViewModel tile) { }
    public void ToggleMaximize(VideoTileViewModel tile) { }
    public bool CanDropCameras => false;
    public void DropCameras(VideoTileViewModel tile, IReadOnlyList<CameraReference> cameras) { }
    public void ReportMainAspectRatio(CameraReference camera, string aspectRatio) { }
}
