using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Localization;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Core.Security;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Settings page: users, their passwords and permissions. Users are part of the configuration,
/// so changes are synchronized with the settings database like other settings.
/// </summary>
public sealed partial class UsersSettingsViewModel : ViewModelBase, IDisposable
{
    private static readonly UserPermissions[] EditablePermissions =
    {
        UserPermissions.GeneralSettings,
        UserPermissions.Devices,
        UserPermissions.Groups,
        UserPermissions.SettingsSync,
        UserPermissions.Users,
        UserPermissions.Views,
        UserPermissions.Favorites,
        UserPermissions.Playback,
        UserPermissions.Ptz
    };

    private readonly ConfigurationService _configuration;
    private readonly UserSessionService _session;
    private readonly LocalSettingsService _localSettings;
    private readonly IDialogService _dialogs;
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyCanExecuteChangedFor(nameof(DeleteUserCommand))]
    private UserAccount? _selectedUser;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private bool _isNew;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public UsersSettingsViewModel(
        ConfigurationService configuration,
        UserSessionService session,
        LocalSettingsService localSettings,
        IDialogService dialogs)
    {
        _configuration = configuration;
        _session = session;
        _localSettings = localSettings;
        _dialogs = dialogs;
        Permissions = EditablePermissions.Select(permission => new PermissionOptionViewModel(permission)).ToList();

        _configuration.Changed += OnConfigurationChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
        LoadUsers(null);
    }

    public ObservableCollection<UserAccount> Users { get; } = new();

    public IReadOnlyList<PermissionOptionViewModel> Permissions { get; }

    public bool IsEditing => IsNew || SelectedUser is not null;

    public void Dispose()
    {
        _configuration.Changed -= OnConfigurationChanged;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
    }

    partial void OnSelectedUserChanged(UserAccount? value)
    {
        if (value is null)
        {
            return;
        }

        IsNew = false;
        UserName = value.UserName;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        foreach (var option in Permissions)
        {
            option.IsChecked = value.Has(option.Permission);
        }

        StatusText = string.Empty;
    }

    [RelayCommand]
    private void AddUser()
    {
        SelectedUser = null;
        IsNew = true;
        UserName = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        foreach (var option in Permissions)
        {
            // New users may watch and control cameras; administration is granted explicitly.
            option.IsChecked = option.Permission is UserPermissions.Favorites or UserPermissions.Playback or UserPermissions.Ptz;
        }

        StatusText = string.Empty;
    }

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        if (!_session.CanEditUsers || !IsEditing || _isSaving)
        {
            return;
        }

        var name = UserName.Trim();
        var existing = IsNew ? null : _configuration.FindUser(SelectedUser!.Id);
        if (!IsNew && existing is null)
        {
            StatusText = Localizer.Text("Users_NotFound");
            return;
        }

        var error = Validate(name, existing);
        if (error is not null)
        {
            StatusText = Localizer.Text(error);
            return;
        }

        var user = existing ?? new UserAccount();
        var previousName = user.UserName;
        user.UserName = name;
        user.Permissions = Permissions.Where(option => option.IsChecked)
            .Aggregate(UserPermissions.None, (all, option) => all | option.Permission);
        if (Password.Length > 0)
        {
            PasswordHasher.SetPassword(user, Password);
        }

        if (existing is null)
        {
            _configuration.Current.Users.Add(user);
        }

        _isSaving = true;
        try
        {
            await _configuration.SaveAsync();
            await RenameAutoLoginAsync(previousName, name);
            StatusText = Localizer.Format("Users_Saved", name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = Localizer.Format("General_SaveFailed", exception.Message);
        }
        finally
        {
            _isSaving = false;
        }

        LoadUsers(user.Id);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteUser))]
    private async Task DeleteUserAsync()
    {
        if (!_session.CanEditUsers || SelectedUser is not { } selected || _configuration.FindUser(selected.Id) is not { } user)
        {
            return;
        }

        if (user.Id == _session.CurrentUser?.Id)
        {
            StatusText = Localizer.Text("Users_CannotDeleteSelf");
            return;
        }

        if (user.Has(UserPermissions.Users) && CountUserManagers(except: user.Id) == 0)
        {
            StatusText = Localizer.Text("Users_LastManager");
            return;
        }

        if (!await _dialogs.ConfirmAsync(Localizer.Text("Users_DeleteTitle"), Localizer.Format("Users_DeleteConfirm", user.UserName)))
        {
            return;
        }

        _configuration.Current.Users.Remove(user);
        try
        {
            await _configuration.SaveAsync();
            await RenameAutoLoginAsync(user.UserName, string.Empty);
            StatusText = Localizer.Format("Users_Deleted", user.UserName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = Localizer.Format("General_SaveFailed", exception.Message);
        }

        LoadUsers(null);
    }

    private bool CanDeleteUser() => SelectedUser is not null;

    /// <summary>
    /// Returns the key of the validation message, or null when the edited user can be saved.
    /// </summary>
    private string? Validate(string name, UserAccount? existing)
    {
        if (name.Length == 0)
        {
            return "Users_NameRequired";
        }

        if (_configuration.FindUser(name) is { } sameName && sameName.Id != existing?.Id)
        {
            return "Users_NameTaken";
        }

        if (existing is null && Password.Length == 0)
        {
            return "Users_PasswordRequired";
        }

        if (Password != ConfirmPassword)
        {
            return "Users_PasswordMismatch";
        }

        var keepsUserManagement = Permissions.Any(option => option.Permission == UserPermissions.Users && option.IsChecked);
        if (existing is not null && existing.Has(UserPermissions.Users) && !keepsUserManagement
            && CountUserManagers(except: existing.Id) == 0)
        {
            return "Users_LastManager";
        }

        return null;
    }

    private int CountUserManagers(Guid except)
    {
        return _configuration.Current.Users.Count(user => user.Id != except && user.Has(UserPermissions.Users));
    }

    /// <summary>
    /// Keeps automatic login on this computer pointing at a renamed account, and turns it off for a deleted one.
    /// </summary>
    private async Task RenameAutoLoginAsync(string previousName, string newName)
    {
        if (previousName.Length == 0
            || !string.Equals(_localSettings.Current.AutoLoginUserName, previousName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(previousName, newName, StringComparison.Ordinal))
        {
            return;
        }

        _localSettings.Current.AutoLoginUserName = newName;
        await _localSettings.SaveAsync();
    }

    private void LoadUsers(Guid? selectId)
    {
        Users.Clear();
        foreach (var user in _configuration.Current.Users.OrderBy(user => user.UserName, StringComparer.CurrentCultureIgnoreCase))
        {
            Users.Add(user);
        }

        IsNew = false;
        SelectedUser = Users.FirstOrDefault(user => user.Id == selectId);
        if (SelectedUser is null)
        {
            UserName = string.Empty;
            Password = string.Empty;
            ConfirmPassword = string.Empty;
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        // A configuration received from the settings database replaces the user objects.
        if (!_isSaving && !IsNew)
        {
            LoadUsers(SelectedUser?.Id);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var option in Permissions)
        {
            option.NotifyLabelChanged();
        }
    }
}
