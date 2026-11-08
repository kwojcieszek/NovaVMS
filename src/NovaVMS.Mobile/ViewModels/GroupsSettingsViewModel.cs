using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Configuration;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Camera groups: order, new custom groups and groups created from all cameras of a recorder.</summary>
public sealed partial class GroupsSettingsViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateGroupFromDeviceCommand))]
    private Device? _selectedSourceDevice;

    public override MobileTab Tab => MobileTab.Settings;

    public IReadOnlyList<GroupRowViewModel> Groups { get; }

    public IReadOnlyList<Device> Devices => _shell.Current.Devices;

    public GroupsSettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;

        var groups = shell.Current.Groups;

        Groups = groups.Select((group, index) => new GroupRowViewModel(this, group, index > 0, index < groups.Count - 1)).ToList();
    }

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowSettings();
    }

    internal void Edit(CameraGroup group)
    {
        _shell.EditGroup(group);
    }

    internal async Task MoveAsync(CameraGroup group, int offset)
    {
        var groups = _shell.Current.Groups;
        var index = groups.IndexOf(group);

        if (index < 0 || index + offset < 0 || index + offset >= groups.Count)
        {
            return;
        }

        (groups[index], groups[index + offset]) = (groups[index + offset], group);

        await _shell.SaveConfigurationAsync();

        _shell.ShowGroups();
    }

    private bool HasSelectedSourceDevice()
    {
        return SelectedSourceDevice is not null;
    }

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        var number = _shell.Current.Groups.Count(candidate => candidate.IsCustom) + 1;
        var group = _shell.Current.CreateCustomGroup(Localizer.Format("Tree_DefaultGroupName", number));

        await _shell.SaveConfigurationAsync();

        _shell.EditGroup(group);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSourceDevice))]
    private async Task CreateGroupFromDeviceAsync()
    {
        if (SelectedSourceDevice is not { } device)
        {
            return;
        }

        var group = _shell.Current.CreateGroupFromDevice(device);

        await _shell.SaveConfigurationAsync();

        _shell.EditGroup(group);
    }
}
