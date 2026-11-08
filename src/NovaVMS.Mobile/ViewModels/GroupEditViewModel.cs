using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Configuration;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Name and cameras of one group; changes are saved at once, as on the desktop.</summary>
public sealed partial class GroupEditViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly CameraGroup _group;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private DeviceFilterOption _deviceFilter;

    public override MobileTab Tab => MobileTab.Settings;

    public IReadOnlyList<DeviceFilterOption> DeviceFilters { get; }

    public ObservableCollection<GroupCameraRowViewModel> Cameras { get; } = new();

    public ObservableCollection<GroupCameraRowViewModel> AvailableCameras { get; } = new();

    public GroupEditViewModel(ShellViewModel shell, CameraGroup group)
    {
        _shell = shell;
        _group = group;
        _name = group.Name;

        DeviceFilters = new[] { new DeviceFilterOption(null) }
            .Concat(shell.Current.Devices.Select(device => new DeviceFilterOption(device)))
            .ToList();
        _deviceFilter = DeviceFilters[0];

        Refresh();
    }

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowGroups();
    }

    internal async Task AddAsync(MobileCamera camera)
    {
        if (AppConfigurationExtensions.AddCamerasToGroup(_group, [CameraReference.For(camera.Device, camera.Channel)]) > 0)
        {
            await SaveAndRefreshAsync();
        }
    }

    internal async Task RemoveAsync(MobileCamera camera)
    {
        _group.Cameras.Remove(CameraReference.For(camera.Device, camera.Channel));

        await SaveAndRefreshAsync();
    }

    internal async Task MoveAsync(MobileCamera camera, int offset)
    {
        var reference = CameraReference.For(camera.Device, camera.Channel);
        var index = _group.Cameras.IndexOf(reference);
        var target = index + offset;

        if (index < 0 || target < 0 || target >= _group.Cameras.Count)
        {
            return;
        }

        var moved = _group.Cameras[index];

        _group.Cameras.RemoveAt(index);
        _group.Cameras.Insert(target, moved);

        await SaveAndRefreshAsync();
    }

    private async Task SaveAndRefreshAsync()
    {
        await _shell.SaveConfigurationAsync();

        Refresh();
    }

    private void Refresh()
    {
        var inGroup = new CameraCollection(CameraCollectionKind.Group, _group.Id, _group.Name).Resolve(_shell.Current);

        Cameras.Clear();

        for (var index = 0; index < inGroup.Count; index++)
        {
            Cameras.Add(new GroupCameraRowViewModel(this, inGroup[index], true, index > 0, index < inGroup.Count - 1));
        }

        RefreshAvailableCameras();
    }

    private void RefreshAvailableCameras()
    {
        AvailableCameras.Clear();

        foreach (var device in _shell.Current.Devices)
        {
            foreach (var channel in device.Channels)
            {
                var camera = new MobileCamera(device, channel);

                if (DeviceFilter.Matches(camera) && !_group.Cameras.Contains(CameraReference.For(device, channel)))
                {
                    AvailableCameras.Add(new GroupCameraRowViewModel(this, camera, false, false, false));
                }
            }
        }
    }

    // The group is renamed while the user types; there is no separate confirmation.
    partial void OnNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var name = value.Trim();

        if (string.Equals(_group.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        _group.Name = name;

        _ = _shell.SaveConfigurationAsync();
    }

    partial void OnDeviceFilterChanged(DeviceFilterOption value)
    {
        RefreshAvailableCameras();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!await _shell.ConfirmAsync(Localizer.Text("Mobile_DeleteGroupTitle"), Localizer.Format("Mobile_DeleteGroupConfirm", _group.Name)))
        {
            return;
        }

        _shell.Current.Groups.Remove(_group);

        await _shell.SaveConfigurationAsync();

        _shell.ShowGroups();
    }
}
