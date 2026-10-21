using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Core.Models;
using NovaVMS.Core.Security;

namespace NovaVMS.App.Services;

/// <summary>
/// The logged-in user and what they may do. The account is looked up again whenever the configuration
/// changes, so permissions edited here or received from the settings database apply at once.
/// </summary>
public sealed partial class UserSessionService : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly LocalSettingsService _localSettings;
    private Guid? _userId;

    public UserSessionService(ConfigurationService configuration, LocalSettingsService localSettings)
    {
        _configuration = configuration;
        _localSettings = localSettings;
        _configuration.Changed += (_, _) => Refresh();
    }

    /// <summary>
    /// Raised after a login, a logout or a change of the logged-in user's permissions.
    /// </summary>
    public event EventHandler? Changed;

    public UserAccount? CurrentUser => _userId is { } id ? _configuration.FindUser(id) : null;

    public bool IsLoggedIn => CurrentUser is not null;

    public string UserName => CurrentUser?.UserName ?? string.Empty;

    public bool CanOpenSettings => (Permissions & UserPermissions.AnySettings) != 0;
    public bool CanEditGeneralSettings => Has(UserPermissions.GeneralSettings);
    public bool CanEditDevices => Has(UserPermissions.Devices);
    public bool CanEditGroups => Has(UserPermissions.Groups);
    public bool CanEditSettingsSync => Has(UserPermissions.SettingsSync);
    public bool CanEditUsers => Has(UserPermissions.Users);
    public bool CanEditViews => Has(UserPermissions.Views);
    public bool CanEditFavorites => Has(UserPermissions.Favorites);
    public bool CanPlayback => Has(UserPermissions.Playback);
    public bool CanControlPtz => Has(UserPermissions.Ptz);

    /// <summary>
    /// Whether this computer logs the current user in automatically.
    /// </summary>
    public bool IsAutoLogin => IsLoggedIn
        && string.Equals(_localSettings.Current.AutoLoginUserName, UserName, StringComparison.OrdinalIgnoreCase);

    private UserPermissions Permissions => CurrentUser?.Permissions ?? UserPermissions.None;

    public bool Has(UserPermissions permission) => CurrentUser?.Has(permission) == true;

    public bool TryLogin(string userName, string password)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? null : _configuration.FindUser(userName);
        if (user is null || !PasswordHasher.Verify(user, password))
        {
            return false;
        }

        SetUser(user.Id);
        return true;
    }

    /// <summary>
    /// Logs in the user chosen for automatic login on this computer, if that account still exists.
    /// </summary>
    public bool TryAutoLogin()
    {
        var name = _localSettings.Current.AutoLoginUserName;
        if (string.IsNullOrWhiteSpace(name) || _configuration.FindUser(name) is not { } user)
        {
            return false;
        }

        SetUser(user.Id);
        return true;
    }

    public void Logout()
    {
        SetUser(null);
    }

    /// <summary>
    /// Turns automatic login on this computer on for the current user, or off.
    /// </summary>
    public async Task SetAutoLoginAsync(bool enabled)
    {
        _localSettings.Current.AutoLoginUserName = enabled && IsLoggedIn ? UserName : string.Empty;
        await _localSettings.SaveAsync();
        OnPropertyChanged(nameof(IsAutoLogin));
    }

    private void SetUser(Guid? userId)
    {
        _userId = userId;
        NotifyAll();
    }

    private void Refresh()
    {
        // A removed account ends the session; changed permissions are re-read from the account.
        if (_userId is not null && CurrentUser is null)
        {
            _userId = null;
        }

        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(string.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
