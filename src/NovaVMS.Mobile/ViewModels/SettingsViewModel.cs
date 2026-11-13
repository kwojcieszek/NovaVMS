using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Settings menu with the pages of the desktop settings window, without software updates and user accounts.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private static readonly string VersionNumber = typeof(SettingsViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

    private readonly ShellViewModel _shell;

    public override MobileTab Tab => MobileTab.Settings;

    /// <summary>First page of the settings tab; the tab bar leads back to the cameras.</summary>
    public override bool HasBackButton => false;

    /// <summary>Release version of the application, the same number as the desktop release.</summary>
    public string VersionText => Localizer.Format("Settings_Version", VersionNumber);

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

    [RelayCommand]
    private void OpenLog()
    {
        _shell.ShowLog();
    }
}
