using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Tree of camera groups with search. Favorites are listed first, followed by custom
/// groups and device groups.
/// </summary>
public sealed partial class ResourceTreeViewModel : ViewModelBase
{
    private readonly ConfigurationService _configuration;
    private readonly DeviceSyncService _sync;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<(GroupNodeKind Kind, Guid? Id), bool> _expandedGroups = new();
    private bool _isFiltered;
    private string? _activeGroupKey;
    private Guid? _activeSavedViewId;
    // Unfiltered cameras of every group from the last rebuild, to detect changed group content.
    private Dictionary<string, IReadOnlyList<CameraReference>>? _groupContents;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ResourceNodeViewModel? _selectedNode;

    [ObservableProperty]
    private bool _isSearchVisible = true;

    public ResourceTreeViewModel(
        ConfigurationService configuration,
        DeviceSyncService sync,
        IDialogService dialogs,
        UserSessionService session)
    {
        _configuration = configuration;
        _sync = sync;
        _dialogs = dialogs;
        Session = session;

        _configuration.Changed += OnConfigurationChanged;
        _sync.StatusChanged += OnStatusChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Raised when the user asks to open a camera or a whole group in the live view.
    /// </summary>
    public event EventHandler<ResourceNodeViewModel>? OpenRequested;
    public event EventHandler? NewViewRequested;

    /// <summary>
    /// Permissions of the logged-in user; the tree's menus bind to them.
    /// </summary>
    public UserSessionService Session { get; }
    public event EventHandler<CameraNodeViewModel>? ViewCameraRemoved;

    [RelayCommand]
    private async Task RemoveFromViewAsync(CameraNodeViewModel? node)
    {
        if (!Session.CanEditViews || node?.ParentViewId is not { } id) return;
        var view = _configuration.Current.Views.FirstOrDefault(view => view.Id == id);
        var index = node.ViewSlotIndex;
        if (view is null || index < 0 || index >= view.Layout.Cameras.Count
            || view.Layout.Cameras[index] != node.Reference) return;
        view.Layout.Cameras[index] = null;
        view.Layout.EmptySlots.Add(index);
        ViewCameraRemoved?.Invoke(this, node);
        await _configuration.SaveAsync();
    }

    [RelayCommand]
    private void RequestNewView()
    {
        if (Session.CanEditViews)
        {
            NewViewRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised when the user asks to forget the remembered live view of a group.
    /// </summary>
    public event EventHandler<ResourceGroupNodeViewModel>? ResetViewRequested;

    /// <summary>
    /// Raised after a rebuild for every existing group whose cameras were added, removed or reordered.
    /// </summary>
    public event EventHandler<GroupContentChangedEventArgs>? GroupContentChanged;

    public ObservableCollection<ResourceGroupNodeViewModel> Groups { get; } = new();

    public void SetActiveLiveView(string? groupKey, Guid? savedViewId)
    {
        _activeGroupKey = groupKey;
        _activeSavedViewId = savedViewId;
        RefreshActiveLiveView();
    }

    private IEnumerable<ResourceGroupNodeViewModel> AllGroups => Groups.SelectMany(group =>
        new[] { group }.Concat(group.Views));

    private void RefreshActiveLiveView()
    {
        foreach (var group in AllGroups)
            group.IsActive = group.IsSavedView ? group.Id == _activeSavedViewId
                : group.IsCameraGroup && group.ViewKey == _activeGroupKey;
    }

    public IReadOnlyList<CameraReference> GetCameras(ResourceNodeViewModel node)
    {
        switch (node)
        {
            case CameraNodeViewModel camera:
            {
                return new[] { camera.Reference };
            }
            case ResourceGroupNodeViewModel group:
            {
                return group.Cameras.Select(camera => camera.Reference).ToList();
            }
            default:
            {
                return Array.Empty<CameraReference>();
            }
        }
    }

    [RelayCommand]
    private void OpenNode(ResourceNodeViewModel? node)
    {
        if (node is not null)
        {
            OpenRequested?.Invoke(this, node);
        }
    }

    [RelayCommand]
    private void ResetGroupView(ResourceGroupNodeViewModel? node)
    {
        if (node is not null)
        {
            ResetViewRequested?.Invoke(this, node);
        }
    }

    [RelayCommand]
    private async Task ToggleSearchAsync()
    {
        _configuration.Current.Settings.ShowCameraSearch = !IsSearchVisible;
        await _configuration.SaveAsync();
    }

    // Camera groups are edited only in the settings; the tree edits saved views.
    [RelayCommand]
    private async Task RenameViewAsync(ResourceGroupNodeViewModel? node)
    {
        if (!Session.CanEditViews || node is not { IsSavedView: true, Id: { } viewId })
        {
            return;
        }

        var view = _configuration.Current.Views.FirstOrDefault(candidate => candidate.Id == viewId);
        if (view is null)
        {
            return;
        }

        var name = await _dialogs.PromptTextAsync(Localizer.Text("Views_Rename"), Localizer.Text("Views_NamePrompt"), view.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        view.Name = name.Trim();
        await _configuration.SaveAsync();
    }

    [RelayCommand]
    private async Task DeleteViewAsync(ResourceGroupNodeViewModel? node)
    {
        if (!Session.CanEditViews || node is not { IsSavedView: true, Id: { } viewId })
        {
            return;
        }

        var view = _configuration.Current.Views.FirstOrDefault(candidate => candidate.Id == viewId);
        if (view is null || !await _dialogs.ConfirmAsync(Localizer.Text("Views_Delete"), Localizer.Format("Views_DeleteConfirm", view.Name)))
        {
            return;
        }

        _configuration.Current.Views.Remove(view);
        await _configuration.SaveAsync();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(CameraNodeViewModel? node)
    {
        if (!Session.CanEditFavorites) return;
        var channel = node is null ? null : _configuration.FindCamera(node.Reference)?.Channel;
        if (channel is null) return;
        channel.IsFavorite = !channel.IsFavorite;
        await _configuration.SaveAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        Rebuild();
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        IsSearchVisible = _configuration.Current.Settings.ShowCameraSearch;
        Rebuild();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Rebuild();
    }

    partial void OnIsSearchVisibleChanged(bool value)
    {
        // A hidden search box must not keep filtering the tree.
        if (!value)
        {
            SearchText = string.Empty;
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        foreach (var group in Groups.Concat(Groups.SelectMany(folder => folder.Views)))
        {
            foreach (var camera in group.Cameras)
            {
                camera.IsOnline = IsOnline(_configuration.FindCamera(camera.Reference));
            }
        }
    }

    private void Rebuild()
    {
        var selected = SelectedNode;
        var selectedParent = selected is CameraNodeViewModel selectedCamera
            ? AllGroups.FirstOrDefault(group => group.Cameras.Contains(selectedCamera)) : null;
        RememberExpandedGroups();
        var groups = new List<ResourceGroupNodeViewModel>();

        var filter = SearchText.Trim();
        _isFiltered = filter.Length > 0;
        var contents = new Dictionary<string, IReadOnlyList<CameraReference>>();
        var customGroups = _configuration.Current.Groups.Where(group => group.IsCustom).ToList();

        // Bookmarks are a separate view; they do not change a camera's group membership.
        var favorites = _configuration.GetAllCameras().Where(camera => camera.Channel.IsFavorite).ToList();
        AddGroupNode(groups, null, Localizer.Text("Tree_Favorites"), GroupNodeKind.Favorites, favorites, filter);
        AddGroupContent(contents, null, GroupNodeKind.Favorites, favorites);

        var viewsFolder = new ResourceGroupNodeViewModel(this, null, Localizer.Text("Views_Title"), GroupNodeKind.ViewsFolder)
        {
            IsExpanded = filter.Length > 0 || !_expandedGroups.TryGetValue((GroupNodeKind.ViewsFolder, null), out var expandedViews) || expandedViews
        };
        foreach (var view in _configuration.Current.Views)
        {
            if (filter.Length > 0 && !view.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                && !view.Layout.Cameras.OfType<CameraReference>().Any(reference =>
                    _configuration.FindCamera(reference)?.FullName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true)) continue;
            var node = new ResourceGroupNodeViewModel(this, view.Id, view.Name, GroupNodeKind.SavedView)
            {
                IsExpanded = filter.Length > 0 || (_expandedGroups.TryGetValue((GroupNodeKind.SavedView, view.Id), out var expanded) && expanded)
            };
            for (var index = 0; index < view.Layout.Cameras.Count; index++)
            {
                if (view.Layout.Cameras[index] is not { } reference) continue;
                var camera = _configuration.FindCamera(reference);
                if (camera is null) continue;
                node.Cameras.Add(new CameraNodeViewModel(this, reference, camera.DisplayName, camera.FullName, view.Id)
                    { IsOnline = IsOnline(camera), IsFavorite = camera.Channel.IsFavorite,
                        ParentViewId = view.Id, ViewSlotIndex = index });
            }
            viewsFolder.Views.Add(node);
        }
        groups.Add(viewsFolder);

        var orderedGroups = customGroups.Concat(_configuration.Current.Groups.Where(group => !group.IsCustom));
        foreach (var group in orderedGroups)
        {
            var cameras = group.Cameras
                .Select(reference => _configuration.FindCamera(reference))
                .OfType<CameraDescriptor>()
                .ToList();

            var kind = group.IsCustom ? GroupNodeKind.Custom : GroupNodeKind.Device;
            AddGroupNode(groups, group.Id, group.Name, kind, cameras, filter);
            AddGroupContent(contents, group.Id, kind, cameras);
        }

        SynchronizeGroups(Groups, groups);
        RefreshActiveLiveView();
        SelectedNode = selected switch
        {
            ResourceGroupNodeViewModel group => AllGroups.FirstOrDefault(node => node.Kind == group.Kind && node.Id == group.Id),
            CameraNodeViewModel camera => AllGroups.FirstOrDefault(node => node.Kind == selectedParent?.Kind
                && node.Id == selectedParent?.Id)?.Cameras.FirstOrDefault(node => node.Reference == camera.Reference
                    && node.ViewSlotIndex == camera.ViewSlotIndex),
            _ => null
        };

        var previous = _groupContents;
        _groupContents = contents;
        if (previous is null)
        {
            return;
        }

        foreach (var (key, cameras) in contents)
        {
            if (previous.TryGetValue(key, out var before) && !before.SequenceEqual(cameras))
            {
                GroupContentChanged?.Invoke(this, new GroupContentChangedEventArgs(key, cameras));
            }
        }
    }

    private static void AddGroupContent(
        Dictionary<string, IReadOnlyList<CameraReference>> contents,
        Guid? id,
        GroupNodeKind kind,
        IReadOnlyList<CameraDescriptor> cameras)
    {
        contents[ResourceGroupNodeViewModel.GetViewKey(id, kind)] = cameras.Select(camera => camera.Reference).ToArray();
    }

    private void AddGroupNode(
        List<ResourceGroupNodeViewModel> groups,
        Guid? id,
        string name,
        GroupNodeKind kind,
        IReadOnlyList<CameraDescriptor> cameras,
        string filter)
    {
        var groupMatches = filter.Length == 0 || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
        var node = new ResourceGroupNodeViewModel(this, id, name, kind)
        {
            IsExpanded = filter.Length > 0 || (_expandedGroups.TryGetValue((kind, id), out var expanded)
                && expanded)
        };

        foreach (var camera in cameras)
        {
            if (!groupMatches && !camera.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            node.Cameras.Add(new CameraNodeViewModel(this, camera.Reference, camera.DisplayName, camera.FullName, id)
            {
                IsOnline = IsOnline(camera),
                IsFavorite = camera.Channel.IsFavorite
            });
        }

        // Keep Favorites discoverable when empty, and empty custom groups visible as configured in the settings.
        if (node.Cameras.Count == 0 && (filter.Length > 0 || kind is not (GroupNodeKind.Custom or GroupNodeKind.Favorites)))
        {
            return;
        }

        groups.Add(node);
    }

    private static void SynchronizeGroups(ObservableCollection<ResourceGroupNodeViewModel> current,
        IReadOnlyList<ResourceGroupNodeViewModel> desired)
    {
        var nodes = desired.Select(candidate =>
        {
            var existing = current.FirstOrDefault(node => node.Kind == candidate.Kind
                && node.Id == candidate.Id && node.Name == candidate.Name);
            if (existing is null) return candidate;
            existing.IsExpanded = candidate.IsExpanded;
            SynchronizeGroups(existing.Views, candidate.Views);
            var cameras = candidate.Cameras.Select(camera =>
            {
                var previous = existing.Cameras.FirstOrDefault(node => node.Reference == camera.Reference
                    && node.Name == camera.Name && node.FullName == camera.FullName
                    && node.IsFavorite == camera.IsFavorite && node.ParentGroupId == camera.ParentGroupId
                    && node.ParentViewId == camera.ParentViewId && node.ViewSlotIndex == camera.ViewSlotIndex);
                if (previous is null) return camera;
                previous.IsOnline = camera.IsOnline;
                return previous;
            }).ToList();
            SynchronizeCollection(existing.Cameras, cameras);
            return existing;
        }).ToList();
        SynchronizeCollection(current, nodes);
    }

    // Keep unchanged rows and their containers attached. Clearing the collection resets the
    // TreeView's scroll extent even when AutoScrollToSelectedItem is disabled.
    private static void SynchronizeCollection<T>(ObservableCollection<T> current, IReadOnlyList<T> desired)
        where T : class
    {
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < current.Count && ReferenceEquals(current[index], desired[index])) continue;
            var previousIndex = current.IndexOf(desired[index]);
            if (previousIndex >= 0) current.Move(previousIndex, index);
            else current.Insert(index, desired[index]);
        }
        while (current.Count > desired.Count) current.RemoveAt(current.Count - 1);
    }

    private void RememberExpandedGroups()
    {
        // Search results expand temporarily without changing the user's normal tree state.
        if (_isFiltered)
        {
            return;
        }

        foreach (var group in Groups)
        {
            _expandedGroups[(group.Kind, group.Id)] = group.IsExpanded;
            foreach (var view in group.Views) _expandedGroups[(view.Kind, view.Id)] = view.IsExpanded;
        }
    }

    private bool IsOnline(CameraDescriptor? camera)
    {
        if (camera is null)
        {
            return false;
        }

        return _sync.IsOnline(camera.Device.Id) && camera.Channel.Online;
    }
}
