using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Connection to the shared settings database (PostgreSQL or MySQL) and its version history,
/// the same options as the desktop synchronization page.
/// </summary>
public sealed partial class SyncSettingsViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly SettingsSyncService _sync;

    [ObservableProperty]
    private bool _syncEnabled;

    [ObservableProperty]
    private DatabaseTypeOption _selectedDatabaseType;

    [ObservableProperty]
    private string _host;

    [ObservableProperty]
    private decimal? _port;

    [ObservableProperty]
    private string _database;

    [ObservableProperty]
    private string _userName;

    [ObservableProperty]
    private string _password;

    [ObservableProperty]
    private string _encryptionPassword;

    [ObservableProperty]
    private string _encryptionPasswordConfirm;

    [ObservableProperty]
    private bool _autoCheck;

    [ObservableProperty]
    private decimal? _checkIntervalMinutes;

    [ObservableProperty]
    private bool _autoUpload;

    [ObservableProperty]
    private decimal? _retainedVersions;

    [ObservableProperty]
    private string _statusText;

    public override MobileTab Tab => MobileTab.Settings;

    public SettingsSyncService Sync => _sync;

    public IReadOnlyList<DatabaseTypeOption> DatabaseTypes => DatabaseTypeOption.All;

    public ObservableCollection<SettingsVersionRowViewModel> Versions { get; } = new();

    public decimal MinimumCheckInterval => SettingsSyncService.MinCheckIntervalMinutes;

    public decimal MaximumCheckInterval => SettingsSyncService.MaxCheckIntervalMinutes;

    public decimal MinimumRetainedVersions => SettingsSyncService.MinRetainedVersions;

    public decimal MaximumRetainedVersions => SettingsSyncService.MaxRetainedVersions;

    public SyncSettingsViewModel(ShellViewModel shell)
    {
        var options = shell.Sync.GetOptions();

        _shell = shell;
        _sync = shell.Sync;
        _syncEnabled = options.Enabled;
        _selectedDatabaseType = DatabaseTypeOption.Find(options.DatabaseType);
        _host = options.Host;
        _port = options.Port;
        _database = options.Database;
        _userName = options.UserName;
        _password = options.Password;
        _encryptionPassword = options.EncryptionPassword;
        _encryptionPasswordConfirm = options.EncryptionPassword;
        _autoCheck = options.AutoCheck;
        _checkIntervalMinutes = options.CheckIntervalMinutes;
        _autoUpload = options.AutoUpload;
        _retainedVersions = options.RetainedVersions;
        _statusText = _sync.StatusText;

        _sync.PropertyChanged += OnSyncPropertyChanged;
        _sync.VersionsChanged += OnVersionsChanged;

        if (_sync.IsActive)
        {
            _ = RefreshAsync();
        }
    }

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowSettings();
    }

    /// <summary>Stops following the synchronization service once the page is replaced.</summary>
    public void Detach()
    {
        _sync.PropertyChanged -= OnSyncPropertyChanged;
        _sync.VersionsChanged -= OnVersionsChanged;
    }

    internal async Task ApplyVersionAsync(SettingsVersionInfo version)
    {
        var message = Localizer.Format("Sync_ApplyConfirm", version.Id,
            version.CreatedAt.ToLocalTime().ToString("g", Localizer.Instance.Culture), version.MachineName);

        if (!await _shell.ConfirmAsync(Localizer.Text("Sync_ApplyTitle"), message))
        {
            return;
        }

        await _sync.ApplyVersionAsync(version);
    }

    private SettingsSyncOptions BuildOptions()
    {
        var options = _sync.GetOptions();

        options.Enabled = SyncEnabled;
        options.DatabaseType = SelectedDatabaseType.Value;
        options.Host = Host.Trim();
        options.Port = (int)Math.Clamp(Port ?? SelectedDatabaseType.DefaultPort, 1, 65535);
        options.Database = Database.Trim();
        options.UserName = UserName.Trim();
        options.Password = Password;
        options.EncryptionPassword = EncryptionPassword;
        options.AutoCheck = AutoCheck;
        options.CheckIntervalMinutes = (int)(CheckIntervalMinutes ?? SettingsSyncOptions.DefaultCheckIntervalMinutes);
        options.AutoUpload = AutoUpload;
        options.RetainedVersions = (int)(RetainedVersions ?? SettingsSyncOptions.DefaultRetainedVersions);

        return options;
    }

    private void OnSyncPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsSyncService.StatusText))
        {
            StatusText = _sync.StatusText;
        }
    }

    private void OnVersionsChanged(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var options = BuildOptions();

        if (options.Enabled)
        {
            if (!options.IsComplete)
            {
                StatusText = Localizer.Text("Sync_MissingFields");

                return;
            }

            if (!string.Equals(EncryptionPassword, EncryptionPasswordConfirm, StringComparison.Ordinal))
            {
                StatusText = Localizer.Text("Sync_PasswordsMismatch");

                return;
            }
        }

        if (!await _sync.SaveOptionsAsync(options))
        {
            return;
        }

        if (!options.Enabled)
        {
            Versions.Clear();

            return;
        }

        var (connected, latest) = await _sync.ConnectAsync();

        if (!connected)
        {
            return;
        }

        if (latest is null)
        {
            if (options.AutoUpload)
            {
                await _sync.UploadAsync();
            }
            else
            {
                StatusText = Localizer.Text("Sync_EmptyDatabase");
            }
        }
        else if (latest.Id > _sync.LastKnownVersionId && latest.InstanceId != _sync.InstanceId)
        {
            // A first connection must not silently replace the settings of this device.
            var message = Localizer.Format("Sync_ExistingConfirm",
                latest.CreatedAt.ToLocalTime().ToString("g", Localizer.Instance.Culture), latest.MachineName, latest.IpAddress);

            if (await _shell.ConfirmAsync(Localizer.Text("Sync_ExistingTitle"), message))
            {
                await _sync.ApplyVersionAsync(latest);
            }
            else
            {
                await _sync.MarkKnownAsync(latest.Id);

                if (options.AutoUpload)
                {
                    await _sync.UploadAsync();
                }
            }
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task UploadNowAsync()
    {
        await _sync.UploadAsync();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        await _sync.CheckForUpdatesAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var versions = await _sync.ListVersionsAsync();

        if (versions is null)
        {
            return;
        }

        Versions.Clear();

        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];

            Versions.Add(new SettingsVersionRowViewModel(this, version, index == 0, version.InstanceId == _sync.InstanceId));
        }
    }

    partial void OnSelectedDatabaseTypeChanged(DatabaseTypeOption? oldValue, DatabaseTypeOption newValue)
    {
        // Follow the server type unless the user entered a non-standard port.
        if (oldValue is not null && (Port is null || Port == oldValue.DefaultPort))
        {
            Port = newValue.DefaultPort;
        }
    }
}
