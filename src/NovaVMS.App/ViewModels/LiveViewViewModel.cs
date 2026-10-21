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
using Avalonia.Threading;

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
    private readonly IDialogService _dialogs;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveSavedViewName))]
    private Guid? _activeSavedViewId;
    private bool _isShutDown;
    private bool _isPageActive = true;
    private readonly Dictionary<VideoTileViewModel, int> _pendingPlayRequests = new();
    private Task _layoutChange = Task.CompletedTask;
    private int _layoutVersion;
    private string? _activeGroup;
    private IReadOnlyList<CameraReference> _groupCameras = Array.Empty<CameraReference>();
    private HashSet<int> _emptySlots = new();
    private bool _restoringGroup;
    private bool _assigningTiles;
    private int _groupVersion;
    private readonly Dictionary<VideoTileViewModel, CameraReference> _assignments = new();
    private Task? _disposal;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _clientOpenings = new();
    private readonly DispatcherTimer _resizeStreamTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public Task Disposal => _disposal ?? Task.CompletedTask;

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
        StatusBarService statusBar,
        IDialogService dialogs)
    {
        _libVlc = libVlc;
        _configuration = configuration;
        _clientFactory = clientFactory;
        _statusBar = statusBar;
        _dialogs = dialogs;
        _resizeStreamTimer.Tick += OnResizeStreamTick;

        _configuration.Changed += OnConfigurationChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
        ApplyLayout(SelectedLayout);
    }

    public IReadOnlyList<GridLayoutOption> Layouts => GridLayoutOption.All;

    public ObservableCollection<VideoTileViewModel> Tiles { get; } = new();

    public CameraReference? SelectedCamera => SelectedTile?.Camera;

    public bool IsMaximized => MaximizedTile is not null;
    public string ActiveSavedViewName => _configuration.Current.Views.FirstOrDefault(view => view.Id == ActiveSavedViewId)?.Name ?? string.Empty;

    public void ReportVideoHeight(VideoTileViewModel tile)
    {
        if (_isShutDown || !Tiles.Contains(tile)) return;
        _resizeStreamTimer.Stop();
        _resizeStreamTimer.Start();
    }

    private void OnResizeStreamTick(object? sender, EventArgs e)
    {
        _resizeStreamTimer.Stop();
        RefreshStreams();
    }

    public void ApplyDefaultLayout()
    {
        SelectedLayout = GridLayoutOption.Find(_configuration.Current.Settings.DefaultLayoutId, _configuration.Current.Settings.DefaultLayoutCount);
    }

    public async void ShowGroup(string key, IReadOnlyList<CameraReference> cameras)
    {
        RememberSavedView();
        ActiveSavedViewId = null;
        if (_isShutDown || (_activeGroup == key && _restoringGroup)) return;
        if (_activeGroup == key)
        {
            _groupCameras = cameras.ToArray();
            FillGroupSlots();
            return;
        }
        RememberGroup();
        var version = ++_groupVersion;
        _restoringGroup = true;
        _activeGroup = key;
        _groupCameras = cameras.ToArray();
        var saved = _configuration.Current.GroupLiveViews.GetValueOrDefault(key)?.Copy();
        try
        {
            _assignments.Clear();
            _emptySlots = saved?.EmptySlots.ToHashSet() ?? new();
            MaximizedTile = null;
            if (saved is not null) SelectedLayout = GridLayoutOption.Find(saved.LayoutId, saved.LayoutCount);
            await _layoutChange;
            if (_isShutDown || version != _groupVersion) return;
            var slots = saved?.Cameras ?? cameras.Cast<CameraReference?>().ToList();
            var targets = Tiles.Select((_, index) => index < slots.Count && slots[index] is { } camera
                && _configuration.FindCamera(camera) is not null ? camera : null).ToList();
            AssignTiles(targets);
            OnPropertyChanged(nameof(SelectedCamera));
            FillGroupSlots();
            SelectedTile = Tiles.FirstOrDefault();
        }
        catch (Exception exception) { DeviceOperationErrors.Report("Restoring group view", exception); }
        finally
        {
            if (version == _groupVersion) _restoringGroup = false;
        }
    }

    /// <summary>
    /// Forgets the remembered split and camera placement of a group. A group shown at the moment
    /// is rearranged at once: its cameras fill the current split in the group's order.
    /// </summary>
    public void ResetGroupView(string key, IReadOnlyList<CameraReference> cameras)
    {
        if (_isShutDown)
        {
            return;
        }

        var removed = _configuration.Current.GroupLiveViews.Remove(key);
        if (_activeGroup == key && !_restoringGroup)
        {
            _groupCameras = cameras.ToArray();
            _emptySlots = new();
            MaximizedTile = null;
            var available = _groupCameras.Where(camera => _configuration.FindCamera(camera) is not null).ToArray();
            AssignTiles(Tiles.Select((_, index) => index < available.Length ? available[index] : null).ToList());
            SelectedTile = Tiles.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedCamera));
        }

        if (removed)
        {
            _ = SaveGroupViewsAsync();
        }
    }

    private void RememberGroup()
    {
        if (_activeGroup is null || _restoringGroup) return;
        _configuration.Current.GroupLiveViews[_activeGroup] = new GroupLiveView
        {
            LayoutId = SelectedLayout.Id,
            LayoutCount = SelectedLayout.Count,
            EmptySlots = _emptySlots.ToHashSet(),
            Cameras = Tiles.Take(SelectedLayout.Count)
                .Select(tile => _assignments.GetValueOrDefault(tile) ?? tile.Camera).ToList()
        };
        _ = SaveGroupViewsAsync();
    }

    private GroupLiveView CaptureView() => new()
    {
        LayoutId = SelectedLayout.Id,
        LayoutCount = SelectedLayout.Count,
        EmptySlots = _emptySlots.ToHashSet(),
        Cameras = Enumerable.Range(0, SelectedLayout.Count).Select(index => index < Tiles.Count
            ? _assignments.GetValueOrDefault(Tiles[index]) ?? Tiles[index].Camera : null).ToList()
    };

    private void RememberSavedView()
    {
        if (_isShutDown || _restoringGroup || _assigningTiles) return;
        var view = _configuration.Current.Views.FirstOrDefault(candidate => candidate.Id == ActiveSavedViewId);
        if (view is null) return;
        var maximized = MaximizedTile is { } tile && tile.Index < SelectedLayout.Count ? (int?)tile.Index : null;
        if (SavedViewChanges.Apply(view, CaptureView(), maximized)) _ = SaveGroupViewsAsync();
    }

    [RelayCommand]
    private async Task SaveNewViewAsync()
    {
        if (_isShutDown || _restoringGroup) return;
        var name = await _dialogs.PromptTextAsync(Localizer.Text("Views_SaveNew"), Localizer.Text("Views_NamePrompt"),
            Localizer.Format("Views_DefaultName", _configuration.Current.Views.Count + 1));
        if (string.IsNullOrWhiteSpace(name) || _isShutDown) return;
        RememberSavedView();
        RememberGroup();
        var view = new SavedCameraView { Name = name.Trim(), Layout = CaptureView(), MaximizedTileIndex = MaximizedTile?.Index };
        _configuration.Current.Views.Add(view);
        _activeGroup = null;
        _groupCameras = Array.Empty<CameraReference>();
        ActiveSavedViewId = view.Id;
        await _configuration.SaveAsync();
        _statusBar.Message = Localizer.Format("Views_Saved", view.Name);
    }

    public async Task ShowSavedViewAsync(Guid id)
    {
        RememberSavedView();
        var view = _configuration.Current.Views.FirstOrDefault(candidate => candidate.Id == id)?.Copy();
        if (view is null || _isShutDown) return;
        RememberGroup();
        var version = ++_groupVersion;
        _activeGroup = null;
        _groupCameras = Array.Empty<CameraReference>();
        ActiveSavedViewId = null;
        _restoringGroup = true;
        try
        {
            MaximizedTile = null;
            _assignments.Clear();
            _emptySlots = view.Layout.EmptySlots.ToHashSet();
            SelectedLayout = GridLayoutOption.Find(view.Layout.LayoutId, view.Layout.LayoutCount);
            await _layoutChange;
            if (_isShutDown || version != _groupVersion) return;
            AssignTiles(Tiles.Select((_, index) => index < view.Layout.Cameras.Count
                && view.Layout.Cameras[index] is { } camera && _configuration.FindCamera(camera) is not null ? camera : null).ToList());
            SelectedTile = Tiles.FirstOrDefault();
            if (view.MaximizedTileIndex is { } maximized && maximized >= 0 && maximized < Tiles.Count)
                MaximizedTile = Tiles[maximized];
            ActiveSavedViewId = id;
            _statusBar.Message = Localizer.Format("Views_Opened", view.Name);
        }
        finally
        {
            if (version == _groupVersion) _restoringGroup = false;
        }
    }

    private async Task SaveGroupViewsAsync()
    {
        try { await _configuration.SaveAsync(); }
        catch (Exception exception) { DeviceOperationErrors.Report("Saving group views", exception); }
    }

    private void FillGroupSlots()
    {
        if (_isShutDown || _activeGroup is null) return;
        var current = Tiles.Select(tile => _assignments.GetValueOrDefault(tile) ?? tile.Camera).ToArray();
        var cameras = _groupCameras.Where(camera => _configuration.FindCamera(camera) is not null).ToArray();
        var filled = GroupCameraPlacement.Fill(current, cameras, _emptySlots);
        for (var index = 0; index < filled.Length; index++)
            if (current[index] is null && filled[index] is { } camera)
                PlayOnTile(Tiles[index], camera);
    }

    /// <summary>
    /// Restores the grid split when a single tile is maximized.
    /// </summary>
    [RelayCommand]
    public void RestoreLayout()
    {
        if (!_isShutDown) MaximizedTile = null;
    }

    /// <summary>
    /// Opens a camera in the selected tile, or in the first free tile when the selected one is busy.
    /// </summary>
    public void OpenCamera(CameraReference camera)
    {
        var target = SelectedTile;
        if (target is null || target.HasCamera || HasPendingPlay(target))
        {
            target = Tiles.FirstOrDefault(tile => !tile.HasCamera && !HasPendingPlay(tile)) ?? target ?? Tiles.FirstOrDefault();
        }

        if (target is null)
        {
            return;
        }

        PlayOnTile(target, camera);
        SelectedTile = target;
    }

    /// <summary>
    /// Shows given cameras (e.g. a whole group) in the currently selected split.
    /// Cameras beyond the grid capacity are reported without changing the user's layout.
    /// Nothing is reopened when exactly these cameras are already shown.
    /// </summary>
    public void ShowCameras(IReadOnlyList<CameraReference> cameras)
    {
        if (cameras.Count == 0 || IsShowing(cameras))
        {
            return;
        }

        MaximizedTile = null;
        foreach (var tile in Tiles) _emptySlots.Add(tile.Index);
        AssignTiles(Tiles.Select((_, index) => index < cameras.Count ? cameras[index] : null).ToList());

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

        RememberSavedView();
        _isShutDown = true;
        _resizeStreamTimer.Stop();
        _resizeStreamTimer.Tick -= OnResizeStreamTick;
        RememberGroup();
        _lifetime.Cancel();
        _configuration.Changed -= OnConfigurationChanged;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        foreach (var tile in Tiles)
        {
            tile.Close();
        }
    }

    public void Dispose() => _disposal ??= DisposeCoreAsync();

    private async Task DisposeCoreAsync()
    {
        Shutdown();
        try
        {
            await _layoutChange;
            await Task.WhenAll(_clientOpenings);
        }
        catch (Exception exception) { UnhandledExceptionLogger.Log("Finishing camera operations", exception); }
        var tiles = Tiles.ToArray();
        foreach (var tile in tiles) tile.Dispose();
        await Task.WhenAll(tiles.Select(tile => tile.Disposal));
        Tiles.Clear();
    }

    public void SelectTile(VideoTileViewModel tile)
    {
        SelectedTile = tile;
    }

    public void ToggleMaximize(VideoTileViewModel tile)
    {
        if (_isShutDown || !Tiles.Contains(tile)) return;
        SelectedTile = tile;
        // Resize the live surface immediately. The tile keeps the outgoing stream playing
        // while the replacement stream connects and renders its first pictures.
        MaximizedTile = ReferenceEquals(MaximizedTile, tile) ? null : tile;
    }

    public async void ReportMainAspectRatio(CameraReference camera, string aspectRatio)
    {
        var channel = _configuration.FindCamera(camera)?.Channel;
        if (channel is null || channel.MainAspectRatio == aspectRatio)
        {
            return;
        }

        channel.MainAspectRatio = aspectRatio;
        try { await _configuration.SaveAsync(); }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            _statusBar.Message = DeviceOperationErrors.Report("Saving camera proportions", exception);
        }
    }

    /// <summary>
    /// A group's tiles follow its configured cameras (edited in the settings); only views and
    /// free layouts accept cameras dragged from the tree.
    /// </summary>
    public bool CanDropCameras => _activeGroup is null;

    public void DropCameras(VideoTileViewModel tile, IReadOnlyList<CameraReference> cameras)
    {
        if (!CanDropCameras)
        {
            return;
        }

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
        foreach (var tile in Tiles) _emptySlots.Add(tile.Index);
        ClearTiles();
        RememberSavedView();
    }

    /// <summary>
    /// Shows the given camera on each tile (null closes it). A tile already showing its camera keeps
    /// the open stream instead of reconnecting.
    /// </summary>
    private void AssignTiles(IReadOnlyList<CameraReference?> cameras)
    {
        _assigningTiles = true;
        try { AssignTilesCore(cameras); }
        finally { _assigningTiles = false; }
        RememberSavedView();
    }

    private void AssignTilesCore(IReadOnlyList<CameraReference?> cameras)
    {
        _assignments.Clear();
        for (var index = 0; index < Tiles.Count; index++)
        {
            var tile = Tiles[index];
            var camera = index < cameras.Count ? cameras[index] : null;
            if (camera is null)
            {
                tile.Close();
            }
            else if (Equals(tile.Camera, camera) && !HasPendingPlay(tile))
            {
                _assignments[tile] = camera;
                _emptySlots.Remove(tile.Index);
            }
            else
            {
                PlayOnTile(tile, camera);
            }
        }
    }

    /// <summary>
    /// Called when the live view page is shown or hidden. Streams of a hidden page are released
    /// after the tiles' grace period and reopened when the page is shown again.
    /// </summary>
    public void SetPageActive(bool active)
    {
        if (_isShutDown || _isPageActive == active) return;
        _isPageActive = active;
        UpdateTileVisibility();
    }

    private void UpdateTileVisibility()
    {
        foreach (var tile in Tiles)
        {
            tile.IsVisibleInGrid = _isPageActive && (MaximizedTile is null || ReferenceEquals(tile, MaximizedTile));
        }
    }

    private void ClearTiles()
    {
        _assignments.Clear();
        foreach (var tile in Tiles)
        {
            tile.Close();
        }

        OnPropertyChanged(nameof(SelectedCamera));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTile))]
    private void CloseSelected()
    {
        if (SelectedTile is not null)
        {
            _assignments.Remove(SelectedTile);
            _emptySlots.Add(SelectedTile.Index);
        }
        SelectedTile?.Close();
        RememberSavedView();
        OnPropertyChanged(nameof(SelectedCamera));
    }

    public void RemoveViewCamera(Guid? viewId, int index)
    {
        if (ActiveSavedViewId != viewId || index < 0 || index >= Tiles.Count) return;
        var tile = Tiles[index];
        _assignments.Remove(tile);
        _emptySlots.Add(index);
        tile.Close();
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
    private async Task TakeSnapshotAsync()
    {
        var tile = SelectedTile;
        if (tile is null || tile.Status != TileStatus.Playing)
        {
            _statusBar.Message = Localizer.Text("Live_SnapshotNeedsStream");
            return;
        }

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NovaVMS");

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat((tile.CameraName ?? "camera").Select(character => invalidCharacters.Contains(character) ? '_' : character));
        var filePath = Path.Combine(directory, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        try
        {
            _statusBar.Message = await tile.TakeSnapshotAsync(filePath)
                ? Localizer.Format("Live_SnapshotSaved", filePath)
                : Localizer.Text("Live_SnapshotFailed");
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            _statusBar.Message = DeviceOperationErrors.Report("Camera snapshot", exception);
        }
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
        if (MaximizedTile is not null)
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
        MaximizedTile = null;
        ApplyLayout(value);
        RememberSavedView();
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
        UpdateTileVisibility();

        Rows = newValue is null ? SelectedLayout.Rows : 1;
        Columns = newValue is null ? SelectedLayout.Columns : 1;

        // A maximized tile switches to the main stream and returns to the grid stream when restored.
        RefreshStreams();
        RememberSavedView();
    }

    partial void OnShowTileTitlesChanged(bool value)
    {
        foreach (var tile in Tiles)
        {
            tile.ShowTitle = value;
        }
    }

    private async void ApplyLayout(GridLayoutOption layout)
    {
        var version = ++_layoutVersion;
        var previous = _layoutChange;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _layoutChange = completion.Task;
        try
        {
            await previous;
            if (_isShutDown || version != _layoutVersion) return;
            MaximizedTile = null;

            // Removed tiles stop their players in parallel; each one waits for its own native stop.
            var removed = Tiles.Skip(layout.Count).ToList();
            foreach (var tile in removed)
            {
                tile.Dispose();
            }

            await Task.WhenAll(removed.Select(tile => tile.Disposal));
            foreach (var tile in removed)
            {
                Tiles.Remove(tile);
                _assignments.Remove(tile);
                _pendingPlayRequests.Remove(tile);
                if (ReferenceEquals(SelectedTile, tile))
                {
                    SelectedTile = null;
                }
            }

            if (_isShutDown || version != _layoutVersion) return;

            while (Tiles.Count < layout.Count)
            {
                Tiles.Add(new VideoTileViewModel(_libVlc, Tiles.Count, this)
                {
                    ShowTitle = ShowTileTitles,
                    HiddenStreamTimeout = HiddenStreamTimeout,
                    IsVisibleInGrid = _isPageActive
                });
            }

            Rows = layout.Rows;
            Columns = layout.Columns;
            SelectedTile ??= Tiles.FirstOrDefault();

            RefreshStreams();
            if (!_restoringGroup) FillGroupSlots();
        }
        catch (Exception exception) { DeviceOperationErrors.Report("Changing camera layout", exception); }
        finally
        {
            completion.TrySetResult();
            if (version == _layoutVersion) RememberSavedView();
        }
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
        return _configuration.Current.Settings.GetLiveStreamType(tile.VideoHeight,
            Tiles.Count == 1 || ReferenceEquals(tile, MaximizedTile));
    }

    private bool HasPendingPlay(VideoTileViewModel tile) => _pendingPlayRequests.TryGetValue(tile, out var version)
        && version == tile.PlayRequestVersion;

    private void PlayOnTile(VideoTileViewModel tile, CameraReference reference)
    {
        tile.ShowLoading();
        _assignments[tile] = reference;
        _emptySlots.Remove(tile.Index);
        RememberSavedView();
        _clientOpenings.RemoveAll(task => task.IsCompleted);
        _clientOpenings.Add(PlayOnTileAsync(tile, reference));
    }

    private async Task PlayOnTileAsync(VideoTileViewModel tile, CameraReference reference)
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

        var requestVersion = tile.ReservePlayRequest();
        _pendingPlayRequests[tile] = requestVersion;
        // Capture UI-owned state before starting an independent worker for this tile.
        var streamType = ResolveStreamType(tile);
        var device = camera.Device.ForHost(camera.Device.Host);
        try
        {
            var source = await LiveSourceLoader.OpenAsync(_clientFactory, device,
                reference.ChannelNumber, streamType, _lifetime.Token);
            if (_isShutDown || tile.PlayRequestVersion != requestVersion || !Tiles.Contains(tile)
                || !ReferenceEquals(_configuration.FindCamera(reference)?.Device, camera.Device)) return;
            if (streamType != ResolveStreamType(tile))
            {
                PlayOnTile(tile, reference);
                return;
            }
            var request = new TilePlayRequest(
                source,
                streamType,
                camera.Device.Username,
                camera.Device.Password,
                _configuration.Current.Settings.GetNetworkCachingMs(streamType),
                camera.Channel.MainAspectRatio);

            tile.Play(reference, camera.FullName, request);
        }
        catch (OperationCanceledException) when (_isShutDown) { }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            if (_isShutDown || tile.PlayRequestVersion != requestVersion) return;
            tile.Close();
            _statusBar.Message = DeviceOperationErrors.Report($"Opening camera: {camera.FullName}", exception);
        }
        finally
        {
            if (_pendingPlayRequests.TryGetValue(tile, out var pending) && pending == requestVersion)
                _pendingPlayRequests.Remove(tile);
        }

        if (ReferenceEquals(tile, SelectedTile))
        {
            OnPropertyChanged(nameof(SelectedCamera));
        }
    }

    private TimeSpan HiddenStreamTimeout => TimeSpan.FromSeconds(Math.Max(0, _configuration.Current.Settings.HiddenTileReleaseSeconds));

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        if (ActiveSavedViewId is { } viewId && !_configuration.Current.Views.Any(view => view.Id == viewId)) ActiveSavedViewId = null;
        OnPropertyChanged(nameof(ActiveSavedViewName));
        ShowTileTitles = _configuration.Current.Settings.ShowTileTitles;

        foreach (var tile in Tiles)
        {
            tile.HiddenStreamTimeout = HiddenStreamTimeout;
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

