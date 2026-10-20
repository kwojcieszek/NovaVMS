namespace NovaVMS.App.ViewModels;

/// <summary>
/// Settings window composed of pages.
/// </summary>
public sealed class SettingsWindowViewModel : ViewModelBase, IDisposable
{
    public SettingsWindowViewModel(
        DevicesSettingsViewModel devices,
        GroupsSettingsViewModel groups,
        GeneralSettingsViewModel general)
    {
        Devices = devices;
        Groups = groups;
        General = general;
    }

    public DevicesSettingsViewModel Devices { get; }

    public GroupsSettingsViewModel Groups { get; }

    public GeneralSettingsViewModel General { get; }

    public void Dispose()
    {
        Devices.Dispose();
        Groups.Dispose();
        General.Dispose();
    }
}
