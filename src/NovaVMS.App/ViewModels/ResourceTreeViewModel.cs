using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Tree of camera groups with search. Custom groups are listed first, followed by
/// groups created from devices and finally cameras that belong to no group.
/// </summary>
public sealed partial class ResourceTreeViewModel : ViewModelBase
{
    private const string UnassignedGroupKey = "Tree_Unassigned";

    private readonly ConfigurationService _configuration;
    private readonly DeviceSyncService _sync;
    private readonly IDialogService _dialogs;
    private readonly StatusBarService _statusBar;
    private readonly HashSet<Guid?> _expandedGroups = new();
    private bool _isFiltered;

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
        StatusBarService statusBar)
    {
        _configuration = configuration;
        _sync = sync;
        _dialogs = dialogs;
        _statusBar = statusBar;

        _configuration.Changed += OnConfigurationChanged;
        _sync.StatusChanged += OnStatusChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Raised when the user asks to open a camera or a whole group in the live view.
    /// </summary>
    public event EventHandler<ResourceNodeViewModel>? OpenRequested;

    public ObservableCollection<ResourceGroupNodeViewModel> Groups { get; } = new();

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

    /// <summary>
    /// Returns the custom group that cameras dropped onto the node should be added to.
    /// </summary>
    public ResourceGroupNodeViewModel? GetDropTarget(ResourceNodeViewModel? node)
    {
        switch (node)
        {
            case ResourceGroupNodeViewModel group when group.IsCustom:
            {
                return group;
            }
            case CameraNodeViewModel camera:
            {
                return Groups.FirstOrDefault(group => group.IsCustom && group.Id == camera.ParentGroupId);
            }
            default:
            {
                return null;
            }
        }
    }

    public async Task AddCamerasToGroupAsync(Guid groupId, IReadOnlyList<CameraReference> cameras)
    {
        var group = _configuration.FindGroup(groupId);
        if (group is null || !group.IsCustom)
        {
            return;
        }

        var added = _configuration.AddCamerasToGroup(group, cameras);
        if (added > 0)
        {
            await _configuration.SaveAsync();
        }

        _statusBar.Message = added == 0
            ? Localizer.Format("Tree_AlreadyInGroup", group.Name)
            : Localizer.Format("Tree_AddedToGroup", added, group.Name);
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
    private async Task ToggleSearchAsync()
    {
        _configuration.Current.Settings.ShowCameraSearch = !IsSearchVisible;
        await _configuration.SaveAsync();
    }

    [RelayCommand]
    private async Task AddCustomGroupAsync()
    {
        var suggestedName = Localizer.Format("Tree_DefaultGroupName", _configuration.Current.Groups.Count(group => group.IsCustom) + 1);
        var name = await _dialogs.PromptTextAsync(Localizer.Text("Tree_NewGroupTitle"), Localizer.Text("Tree_GroupNamePrompt"), suggestedName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var group = _configuration.CreateCustomGroup(name.Trim());
        await _configuration.SaveAsync();

        SelectedNode = Groups.FirstOrDefault(node => node.Id == group.Id);
        _statusBar.Message = Localizer.Format("Tree_GroupCreated", group.Name);
    }

    [RelayCommand]
    private async Task RenameGroupAsync(ResourceGroupNodeViewModel? node)
    {
        var group = node?.Id is Guid id ? _configuration.FindGroup(id) : null;
        if (group is null)
        {
            return;
        }

        var name = await _dialogs.PromptTextAsync(Localizer.Text("Tree_RenameTitle"), Localizer.Text("Tree_GroupNamePrompt"), group.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        group.Name = name.Trim();
        await _configuration.SaveAsync();
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(ResourceGroupNodeViewModel? node)
    {
        var group = node?.Id is Guid id ? _configuration.FindGroup(id) : null;
        if (group is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(Localizer.Text("Tree_DeleteTitle"), Localizer.Format("Tree_DeleteConfirm", group.Name));
        if (!confirmed)
        {
            return;
        }

        _configuration.Current.Groups.Remove(group);
        await _configuration.SaveAsync();
        _statusBar.Message = Localizer.Format("Tree_GroupDeleted", group.Name);
    }

    [RelayCommand]
    private async Task RemoveFromGroupAsync(CameraNodeViewModel? node)
    {
        var group = node?.ParentGroupId is Guid id ? _configuration.FindGroup(id) : null;
        if (node is null || group is null || !group.IsCustom)
        {
            return;
        }

        group.Cameras.Remove(node.Reference);
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
        foreach (var group in Groups)
        {
            foreach (var camera in group.Cameras)
            {
                camera.IsOnline = IsOnline(_configuration.FindCamera(camera.Reference));
            }
        }
    }

    private void Rebuild()
    {
        RememberExpandedGroups();
        Groups.Clear();

        var filter = SearchText.Trim();
        _isFiltered = filter.Length > 0;
        var customGroups = _configuration.Current.Groups.Where(group => group.IsCustom).ToList();
        var assigned = new HashSet<CameraReference>();

        var orderedGroups = customGroups.Concat(_configuration.Current.Groups.Where(group => !group.IsCustom));
        foreach (var group in orderedGroups)
        {
            var cameras = group.Cameras
                .Select(reference => _configuration.FindCamera(reference))
                .OfType<CameraDescriptor>()
                .ToList();

            foreach (var camera in cameras)
            {
                assigned.Add(camera.Reference);
            }

            var kind = group.IsCustom ? GroupNodeKind.Custom : GroupNodeKind.Device;
            AddGroupNode(group.Id, group.Name, kind, cameras, customGroups, filter);
        }

        var unassigned = _configuration.GetAllCameras()
            .Where(camera => !assigned.Contains(camera.Reference))
            .ToList();
        if (unassigned.Count > 0)
        {
            AddGroupNode(null, Localizer.Text(UnassignedGroupKey), GroupNodeKind.Unassigned, unassigned, customGroups, filter);
        }
    }

    private void AddGroupNode(
        Guid? id,
        string name,
        GroupNodeKind kind,
        IReadOnlyList<CameraDescriptor> cameras,
        IReadOnlyList<CameraGroup> customGroups,
        string filter)
    {
        var groupMatches = filter.Length == 0 || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
        var node = new ResourceGroupNodeViewModel(this, id, name, kind)
        {
            IsExpanded = filter.Length > 0 || _expandedGroups.Contains(id)
        };

        foreach (var camera in cameras)
        {
            if (!groupMatches && !camera.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            var actions = BuildAddToGroupActions(camera.Reference, id, customGroups);
            node.Cameras.Add(new CameraNodeViewModel(this, camera.Reference, camera.DisplayName, camera.FullName, id, kind == GroupNodeKind.Custom, actions)
            {
                IsOnline = IsOnline(camera)
            });
        }

        // A custom group is shown even when empty, so cameras can be dragged onto it.
        if (node.Cameras.Count == 0 && (filter.Length > 0 || kind != GroupNodeKind.Custom))
        {
            return;
        }

        Groups.Add(node);
    }

    private IReadOnlyList<MenuActionViewModel> BuildAddToGroupActions(CameraReference camera, Guid? currentGroupId, IReadOnlyList<CameraGroup> customGroups)
    {
        return customGroups
            .Where(group => group.Id != currentGroupId)
            .Select(group =>
            {
                var groupId = group.Id;
                return new MenuActionViewModel(group.Name, new AsyncRelayCommand(() => AddCamerasToGroupAsync(groupId, new[] { camera })));
            })
            .ToList();
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
            if (group.IsExpanded)
            {
                _expandedGroups.Add(group.Id);
            }
            else
            {
                _expandedGroups.Remove(group.Id);
            }
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
