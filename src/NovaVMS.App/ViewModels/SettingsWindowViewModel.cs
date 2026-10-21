using NovaVMS.App.Services;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Settings window composed of pages.
/// </summary>
public sealed class SettingsWindowViewModel : ViewModelBase, IDisposable
{
    private IDisposable? _scope;
    private bool _isDisposed;

    public SettingsWindowViewModel(
        DevicesSettingsViewModel devices,
        GroupsSettingsViewModel groups,
        GeneralSettingsViewModel general,
        SyncSettingsViewModel sync,
        UsersSettingsViewModel users,
        UserSessionService session)
    {
        Devices = devices;
        Groups = groups;
        General = general;
        Sync = sync;
        Users = users;
        Session = session;
    }

    public UsersSettingsViewModel Users { get; }

    /// <summary>
    /// Pages are shown only to users with the matching permission.
    /// </summary>
    public UserSessionService Session { get; }

    public DevicesSettingsViewModel Devices { get; }

    public GroupsSettingsViewModel Groups { get; }

    public GeneralSettingsViewModel General { get; }

    public SyncSettingsViewModel Sync { get; }

    /// <summary>
    /// Takes ownership of the dependency injection scope that created this window's pages.
    /// </summary>
    internal void OwnScope(IDisposable scope)
    {
        _scope = scope;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Devices.Dispose();
        Groups.Dispose();
        General.Dispose();
        Sync.Dispose();
        Users.Dispose();

        // Releases the scope's references to the pages (their Dispose methods tolerate a second call).
        var scope = _scope;
        _scope = null;
        scope?.Dispose();
    }
}
