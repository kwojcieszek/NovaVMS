using NovaVMS.Core.Exceptions;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Live view grid with selectable split, single-tile maximize and drag-and-drop of cameras.
/// </summary>
public sealed partial class LiveViewViewModel : ViewModelBase, ITileHost, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clientFactory;
    private readonly StatusBarService _statusBar;
    private bool _isShutDown;

    [ObservableProperty]
    private GridLayoutOption _selectedLayout = GridLayoutOption.Find(4);

    [ObservableProperty]
    private int _rows = 1;

    [ObservableProperty]
    private int _columns = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCamera))]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAudioCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeSnapshotCommand))]
    private VideoTileViewModel? _selectedTile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMaximized))]
    private VideoTileViewModel? _maximizedTile;

    [ObservableProperty]
    private bool _showTileTitles = true;

    public LiveViewViewModel(
        LibVLC libVlc,
        ConfigurationService configuration,
        IDeviceClientFactory clientFactory,
        StatusBarService statusBar)
    {
        _libVlc = libVlc;
        _configuration = configuration;
        _clientFactory = clientFactory;
        _statusBar = statusBar;

        _configuration.Changed += OnConfigurationChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
        ApplyLayout(SelectedLayout);
    }

    public IReadOnlyList<GridLayoutOption> Layouts => GridLayoutOption.All;

    public ObservableCollection<VideoTileViewModel> Tiles { get; } = new();

    public CameraReference? SelectedCamera => SelectedTile?.Camera;

    public bool IsMaximized => MaximizedTile is not null;

    public void ApplyDefaultLayout()
    {
        SelectedLayout = GridLayoutOption.Find(_configuration.Current.Settings.DefaultLayoutCount);
    }

    /// <summary>
    /// Restores the grid split when a single tile is maximized.
    /// </summary>
    [RelayCommand]
    public void RestoreLayout()
    {
        MaximizedTile = null;
    }

    /// <summary>
    /// Opens a camera in the selected tile, or in the first free tile when the selected one is busy.
    /// </summary>
    public void OpenCamera(CameraReference camera)
    {
        var target = SelectedTile;
        if (target is null || target.HasCamera)
        {
            target = Tiles.FirstOrDefault(tile => !tile.HasCamera) ?? target ?? Tiles.FirstOrDefault();
        }

        if (target is null)
        {
            return;
        }

        PlayOnTile(target, camera);
        SelectedTile = target;
    }

    /// <summary>
    /// Shows all given cameras (e.g. a whole group) in the smallest split that fits them.
    /// Nothing is reopened when exactly these cameras are already shown.
    /// </summary>
    public void ShowCameras(IReadOnlyList<CameraReference> cameras)
    {
        if (cameras.Count == 0 || IsShowing(cameras))
        {
            return;
        }

        CloseAll();
        MaximizedTile = null;
        SelectedLayout = GridLayoutOption.FitFor(cameras.Count);

        var count = Math.Min(cameras.Count, Tiles.Count);
        for (var index = 0; index < count; index++)
        {
            PlayOnTile(Tiles[index], cameras[index]);
        }

        SelectedTile = Tiles.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCamera));

        if (cameras.Count > Tiles.Count)
        {
            _statusBar.Message = Localizer.Format("Live_OpenedPartially", Tiles.Count, cameras.Count);
        }
    }

    /// <summary>
    /// Stops every stream before the window and its native video surfaces are destroyed.
    /// </summary>
    public void Shutdown()
    {
        if (_isShutDown)
        {
            return;
        }

        _isShutDown = true;
        _configuration.Changed -= OnConfigurationChanged;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        foreach (var tile in Tiles)
        {
            tile.Close();
        }
    }

    public void Dispose()
    {
        Shutdown();
        foreach (var tile in Tiles)
        {
            tile.Dispose();
        }

        Tiles.Clear();
    }

    public void SelectTile(VideoTileViewModel tile)
    {
        SelectedTile = tile;
    }

    public void ToggleMaximize(VideoTileViewModel tile)
    {
        SelectedTile = tile;
        MaximizedTile = ReferenceEquals(MaximizedTile, tile) ? null : tile;
    }

    public void DropCameras(VideoTileViewModel tile, IReadOnlyList<CameraReference> cameras)
    {
        if (cameras.Count == 1)
        {
            PlayOnTile(tile, cameras[0]);
            SelectedTile = tile;
            return;
        }

        ShowCameras(cameras);
    }

    [RelayCommand]
    private void CloseAll()
    {
        foreach (var tile in Tiles)
        {
            tile.Close();
        }

        OnPropertyChanged(nameof(SelectedCamera));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTile))]
    private void CloseSelected()
    {
        SelectedTile?.Close();
        OnPropertyChanged(nameof(SelectedCamera));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTile))]
    private void ToggleAudio()
    {
        var selected = SelectedTile;
        if (selected is null)
        {
            return;
        }

        var enable = !selected.IsAudioEnabled;
        foreach (var tile in Tiles)
        {
            tile.IsAudioEnabled = false;
        }

        selected.IsAudioEnabled = enable;
        _statusBar.Message = enable ? Localizer.Format("Live_AudioOn", selected.Number) : Localizer.Text("Live_AudioOff");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTile))]
    private void TakeSnapshot()
    {
        var tile = SelectedTile;
        if (tile is null || tile.Status != TileStatus.Playing)
        {
            _statusBar.Message = Localizer.Text("Live_SnapshotNeedsStream");
            return;
        }

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NovaVMS");
        Directory.CreateDirectory(directory);

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat((tile.CameraName ?? "camera").Select(character => invalidCharacters.Contains(character) ? '_' : character));
        var filePath = Path.Combine(directory, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        _statusBar.Message = tile.TakeSnapshot(filePath)
            ? Localizer.Format("Live_SnapshotSaved", filePath)
            : Localizer.Text("Live_SnapshotFailed");
    }

    [RelayCommand]
    private async Task ToggleTileTitlesAsync()
    {
        _configuration.Current.Settings.ShowTileTitles = !ShowTileTitles;
        await _configuration.SaveAsync();
    }

    private bool HasSelectedTile()
    {
        return SelectedTile is not null;
    }

    private bool IsShowing(IReadOnlyList<CameraReference> cameras)
    {
        if (MaximizedTile is not null || SelectedLayout != GridLayoutOption.FitFor(cameras.Count))
        {
            return false;
        }

        for (var index = 0; index < Tiles.Count; index++)
        {
            var expected = index < cameras.Count ? cameras[index] : null;
            if (!Equals(Tiles[index].Camera, expected))
            {
                return false;
            }
        }

        return true;
    }

    partial void OnSelectedLayoutChanged(GridLayoutOption value)
    {
        ApplyLayout(value);
    }

    partial void OnSelectedTileChanged(VideoTileViewModel? oldValue, VideoTileViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    partial void OnMaximizedTileChanged(VideoTileViewModel? oldValue, VideoTileViewModel? newValue)
    {
        foreach (var tile in Tiles)
        {
            tile.IsVisibleInGrid = newValue is null || ReferenceEquals(tile, newValue);
        }

        Rows = newValue is null ? SelectedLayout.Rows : 1;
        Columns = newValue is null ? SelectedLayout.Columns : 1;

        // A maximized tile switches to the main stream and returns to the grid stream when restored.
        RefreshStreams();
    }

    partial void OnShowTileTitlesChanged(bool value)
    {
        foreach (var tile in Tiles)
        {
            tile.ShowTitle = value;
        }
    }

    private void ApplyLayout(GridLayoutOption layout)
    {
        MaximizedTile = null;

        while (Tiles.Count > layout.Count)
        {
            var tile = Tiles[^1];
            Tiles.RemoveAt(Tiles.Count - 1);
            if (ReferenceEquals(SelectedTile, tile))
            {
                SelectedTile = null;
            }

            tile.Dispose();
        }

        while (Tiles.Count < layout.Count)
        {
            Tiles.Add(new VideoTileViewModel(_libVlc, Tiles.Count, this)
            {
                ShowTitle = ShowTileTitles
            });
        }

        Rows = layout.Rows;
        Columns = layout.Columns;
        SelectedTile ??= Tiles.FirstOrDefault();

        RefreshStreams();
    }

    /// <summary>
    /// Reopens tiles whose current stream does not match the split or the maximized state.
    /// </summary>
    private void RefreshStreams()
    {
        if (_isShutDown)
        {
            return;
        }

        foreach (var tile in Tiles.ToList())
        {
            if (tile.Camera is not null && tile.ActiveStreamType != ResolveStreamType(tile))
            {
                PlayOnTile(tile, tile.Camera);
            }
        }
    }

    private StreamType ResolveStreamType(VideoTileViewModel tile)
    {
        var mode = _configuration.Current.Settings.GridStreamMode;
        if (mode == GridStreamMode.Auto && ReferenceEquals(tile, MaximizedTile))
        {
            return StreamType.Main;
        }

        switch (mode)
        {
            case GridStreamMode.Main:
            {
                return StreamType.Main;
            }
            case GridStreamMode.Sub:
            {
                return StreamType.Sub;
            }
            default:
            {
                // Every split uses the lower-resolution stream; a single or maximized tile uses main.
                return Tiles.Count > 1 ? StreamType.Sub : StreamType.Main;
            }
        }
    }

    private void PlayOnTile(VideoTileViewModel tile, CameraReference reference)
    {
        if (_isShutDown)
        {
            return;
        }

        var camera = _configuration.FindCamera(reference);
        if (camera is null)
        {
            tile.Close();
            return;
        }

        try
        {
            using var client = _clientFactory.Create(camera.Device);
            var streamType = ResolveStreamType(tile);
            var source = client.GetLiveSource(reference.ChannelNumber, streamType);
            var request = new TilePlayRequest(
                source,
                streamType,
                camera.Device.Username,
                camera.Device.Password,
                _configuration.Current.Settings.GetNetworkCachingMs(streamType));

            tile.Play(reference, camera.FullName, request);
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            tile.Close();
            _statusBar.Message = DeviceOperationErrors.Report($"Opening camera: {camera.FullName}", exception);
        }

        if (ReferenceEquals(tile, SelectedTile))
        {
            OnPropertyChanged(nameof(SelectedCamera));
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        ShowTileTitles = _configuration.Current.Settings.ShowTileTitles;

        foreach (var tile in Tiles)
        {
            if (tile.Camera is not null && _configuration.FindCamera(tile.Camera) is null)
            {
                tile.Close();
            }
        }

        RefreshTileNames();
        RefreshStreams();
        OnPropertyChanged(nameof(SelectedCamera));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Channels without a name are shown under a translated default name.
        RefreshTileNames();
        OnPropertyChanged(nameof(SelectedCamera));
    }

    private void RefreshTileNames()
    {
        foreach (var tile in Tiles)
        {
            if (tile.Camera is null)
            {
                continue;
            }

            var camera = _configuration.FindCamera(tile.Camera);
            if (camera is not null)
            {
                tile.Rename(camera.FullName);
            }
        }
    }
}
