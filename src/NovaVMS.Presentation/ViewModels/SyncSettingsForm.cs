using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;
using NovaVMS.Presentation.Models;
using NovaVMS.Presentation.Services;

namespace NovaVMS.Presentation.ViewModels;

/// <summary>
/// Synchronization settings of the desktop and the mobile application: connection to the shared settings database
/// (PostgreSQL or MySQL), sending, checking and the version history. Connection changes take effect after "Save and connect".
/// </summary>
public sealed partial class SyncSettingsForm : ObservableObject, IDisposable
{
    private readonly SettingsSyncService _sync;
    private readonly Func<string, string, Task<bool>> _confirm;
    private bool _isDisposed;

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

    /// <summary>Row chosen in the desktop table; the phone applies a version with the button of its row.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyVersionCommand))]
    private SettingsVersionRow? _selectedVersion;

    [ObservableProperty]
    private string _statusText;

    public SettingsSyncService Sync => _sync;

    public IReadOnlyList<DatabaseTypeOption> DatabaseTypes => DatabaseTypeOption.All;

    public ObservableCollection<SettingsVersionRow> Versions { get; } = new();

    public decimal MinimumCheckInterval => SettingsSyncService.MinCheckIntervalMinutes;

    public decimal MaximumCheckInterval => SettingsSyncService.MaxCheckIntervalMinutes;

    public decimal MinimumRetainedVersions => SettingsSyncService.MinRetainedVersions;

    public decimal MaximumRetainedVersions => SettingsSyncService.MaxRetainedVersions;

    /// <param name="sync">Synchronization service of the application.</param>
    /// <param name="confirm">Asks the user a question (title, message); true when accepted.</param>
    public SyncSettingsForm(SettingsSyncService sync, Func<string, string, Task<bool>> confirm)
    {
        var options = sync.GetOptions();

        _sync = sync;
        _confirm = confirm;
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
        _statusText = sync.StatusText;

        _sync.PropertyChanged += OnSyncPropertyChanged;
        _sync.VersionsChanged += OnVersionsChanged;

        if (_sync.IsActive)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>Stops following the synchronization service once the page is closed.</summary>
    public void Dispose()
    {
        _isDisposed = true;
        _sync.PropertyChanged -= OnSyncPropertyChanged;
        _sync.VersionsChanged -= OnVersionsChanged;
    }

    /// <summary>Asks the user and replaces the configuration with <paramref name="version"/>.</summary>
    public async Task ApplyVersionAsync(SettingsVersionInfo version)
    {
        var message = Localizer.Format("Sync_ApplyConfirm", version.Id,
            version.CreatedAt.ToLocalTime().ToString("g", Localizer.Instance.Culture), version.MachineName);

        if (!await _confirm(Localizer.Text("Sync_ApplyTitle"), message))
        {
            return;
        }

        await _sync.ApplyVersionAsync(version);
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
            // A first connection must not silently replace the settings of this installation.
            var message = Localizer.Format("Sync_ExistingConfirm",
                latest.CreatedAt.ToLocalTime().ToString("g", Localizer.Instance.Culture), latest.MachineName, latest.IpAddress);

            if (await _confirm(Localizer.Text("Sync_ExistingTitle"), message))
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

        if (_isDisposed || versions is null)
        {
            return;
        }

        var selectedId = SelectedVersion?.Id;

        Versions.Clear();

        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];

            Versions.Add(new SettingsVersionRow(version, index == 0, version.InstanceId == _sync.InstanceId, ApplyVersionAsync));
        }

        SelectedVersion = Versions.FirstOrDefault(row => row.Id == selectedId);
    }

    [RelayCommand(CanExecute = nameof(CanApplyVersion))]
    private async Task ApplyVersionAsync()
    {
        if (SelectedVersion?.Info is { } version)
        {
            await ApplyVersionAsync(version);
        }
    }

    private bool CanApplyVersion()
    {
        return SelectedVersion is not null;
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

    partial void OnSelectedDatabaseTypeChanged(DatabaseTypeOption? oldValue, DatabaseTypeOption newValue)
    {
        // Follow the server type unless the user entered a non-standard port.
        if (oldValue is not null && (Port is null || Port == oldValue.DefaultPort))
        {
            Port = newValue.DefaultPort;
        }
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
        if (!_isDisposed)
        {
            _ = RefreshAsync();
        }
    }
}
