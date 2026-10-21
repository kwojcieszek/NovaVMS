using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Settings page: camera groups (custom groups and groups created from recorders).
/// </summary>
public sealed partial class GroupsSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ConfigurationService _configuration;
    // Set while the lists are rebuilt; the temporary loss of selection must not clear the name being typed.
    private bool _isRefreshing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteGroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
    private CameraGroup? _selectedGroup;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCameraCommand))]
    private CameraDescriptor? _selectedGroupCamera;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCameraCommand))]
    private CameraDescriptor? _selectedAvailableCamera;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateGroupFromDeviceCommand))]
    private Device? _selectedSourceDevice;

    [ObservableProperty]
    private string _editGroupName = string.Empty;

    public GroupsSettingsViewModel(ConfigurationService configuration, GroupCameraPreviewViewModel preview)
    {
        _configuration = configuration;
        Preview = preview;
        _configuration.Changed += OnConfigurationChanged;
        Refresh();
    }

    public GroupCameraPreviewViewModel Preview { get; }

    partial void OnSelectedAvailableCameraChanged(CameraDescriptor? value) { if (!_isRefreshing && value is not null) Preview.Select(value); }
    partial void OnSelectedGroupCameraChanged(CameraDescriptor? value) { if (!_isRefreshing && value is not null) Preview.Select(value); }

    public ObservableCollection<CameraGroup> Groups { get; } = new();

    public ObservableCollection<CameraDescriptor> GroupCameras { get; } = new();

    public ObservableCollection<CameraDescriptor> AvailableCameras { get; } = new();

    public ObservableCollection<Device> Devices { get; } = new();

    public void Dispose()
    {
        _configuration.Changed -= OnConfigurationChanged;
        Preview.Dispose();
    }

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        var group = _configuration.CreateCustomGroup(Localizer.Format("Tree_DefaultGroupName", _configuration.Current.Groups.Count(candidate => candidate.IsCustom) + 1));
        await _configuration.SaveAsync();
        SelectedGroup = Groups.FirstOrDefault(candidate => candidate.Id == group.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    private async Task DeleteGroupAsync()
    {
        var group = SelectedGroup;
        if (group is null)
        {
            return;
        }

        _configuration.Current.Groups.Remove(group);
        SelectedGroup = null;
        await _configuration.SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAddCamera))]
    private async Task AddCameraAsync()
    {
        var group = SelectedGroup;
        var camera = SelectedAvailableCamera;
        if (group is null || camera is null)
        {
            return;
        }

        if (_configuration.AddCamerasToGroup(group, new[] { camera.Reference }) > 0)
        {
            await _configuration.SaveAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCamera))]
    private async Task RemoveCameraAsync()
    {
        var group = SelectedGroup;
        var camera = SelectedGroupCamera;
        if (group is null || camera is null)
        {
            return;
        }

        group.Cameras.Remove(camera.Reference);
        await _configuration.SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSourceDevice))]
    private async Task CreateGroupFromDeviceAsync()
    {
        var device = SelectedSourceDevice;
        if (device is null)
        {
            return;
        }

        var group = _configuration.CreateGroupFromDevice(device);
        await _configuration.SaveAsync();
        SelectedGroup = Groups.FirstOrDefault(candidate => candidate.Id == group.Id);
    }

    private bool HasSelectedGroup()
    {
        return SelectedGroup is not null;
    }

    private bool CanAddCamera()
    {
        return SelectedGroup is not null && SelectedAvailableCamera is not null;
    }

    private bool CanRemoveCamera()
    {
        return SelectedGroup is not null && SelectedGroupCamera is not null;
    }

    private bool HasSelectedSourceDevice()
    {
        return SelectedSourceDevice is not null;
    }

    partial void OnSelectedGroupChanged(CameraGroup? value)
    {
        if (_isRefreshing)
        {
            return;
        }

        Preview.Select(null);
        EditGroupName = value?.Name ?? string.Empty;
        RefreshGroupCameras();
    }

    // The group is renamed while the user types; there is no separate confirmation.
    partial void OnEditGroupNameChanged(string value)
    {
        var group = SelectedGroup;
        if (_isRefreshing || group is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var name = value.Trim();
        if (string.Equals(group.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        group.Name = name;
        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _configuration.SaveAsync();
        }
        catch (IOException)
        {
            // The next change saves the configuration again; nothing is lost in memory.
        }
        catch (UnauthorizedAccessException)
        {
            // The next change saves the configuration again; nothing is lost in memory.
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        var selectedGroupId = SelectedGroup?.Id;
        var selectedDeviceId = SelectedSourceDevice?.Id;

        _isRefreshing = true;
        try
        {
            RebuildLists(selectedGroupId, selectedDeviceId);
        }
        finally
        {
            _isRefreshing = false;
        }

        if (SelectedGroup is null)
        {
            EditGroupName = string.Empty;
        }

        RefreshGroupCameras();
    }

    private void RebuildLists(Guid? selectedGroupId, Guid? selectedDeviceId)
    {
        Groups.Clear();
        foreach (var group in _configuration.Current.Groups.OrderBy(group => group.IsCustom ? 0 : 1))
        {
            Groups.Add(group);
        }

        Devices.Clear();
        foreach (var device in _configuration.Current.Devices)
        {
            Devices.Add(device);
        }

        AvailableCameras.Clear();
        foreach (var camera in _configuration.GetAllCameras())
        {
            AvailableCameras.Add(camera);
        }

        SelectedGroup = Groups.FirstOrDefault(group => group.Id == selectedGroupId);
        SelectedSourceDevice = Devices.FirstOrDefault(device => device.Id == selectedDeviceId);
    }

    private void RefreshGroupCameras()
    {
        GroupCameras.Clear();
        var group = SelectedGroup;
        if (group is null)
        {
            return;
        }

        foreach (var reference in group.Cameras)
        {
            var camera = _configuration.FindCamera(reference);
            if (camera is not null)
            {
                GroupCameras.Add(camera);
            }
        }
    }
}

