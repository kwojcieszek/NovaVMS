using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Settings menu with the pages of the desktop settings window, without software updates and user accounts.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;

    public override MobileTab Tab => MobileTab.Settings;

    /// <summary>First page of the settings tab; the tab bar leads back to the cameras.</summary>
    public override bool HasBackButton => false;

    public SettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
    }

    [RelayCommand]
    private void OpenGeneral()
    {
        _shell.ShowGeneralSettings();
    }

    [RelayCommand]
    private void OpenGroups()
    {
        _shell.ShowGroups();
    }

    [RelayCommand]
    private void OpenDevices()
    {
        _shell.ShowDevices();
    }

    [RelayCommand]
    private void OpenSync()
    {
        _shell.ShowSync();
    }
}
