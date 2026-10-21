using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Keeps the configuration in the shared settings database: sends local changes, periodically applies
/// newer versions sent from other computers and restores archived versions.
/// Used on the UI thread; database operations run one at a time.
/// </summary>
public sealed partial class SettingsSyncService : ObservableObject, IDisposable
{
    public const int MinCheckIntervalMinutes = 1;
    public const int MaxCheckIntervalMinutes = 1440;
    public const int MinRetainedVersions = 1;
    public const int MaxRetainedVersions = 1000;
    private static readonly TimeSpan UploadDelay = TimeSpan.FromSeconds(5);

    private readonly ConfigurationService _configuration;
    private readonly ISettingsSyncOptionsStore _optionsStore;
    private readonly IRemoteSettingsStoreFactory _storeFactory;
    private readonly StatusBarService _statusBar;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DispatcherTimer _checkTimer;
    private readonly DispatcherTimer _uploadTimer;
    private SettingsSyncOptions _options = new();
    private IRemoteSettingsStore? _store;
    // Incremented by every local save, so an upload knows whether newer changes arrived meanwhile.
    private int _localChangeCount;
    private bool _uploadPending;
    private bool _isDisposed;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsSyncService(
        ConfigurationService configuration,
        ISettingsSyncOptionsStore optionsStore,
        IRemoteSettingsStoreFactory storeFactory,
        StatusBarService statusBar)
    {
        _configuration = configuration;
        _optionsStore = optionsStore;
        _storeFactory = storeFactory;
        _statusBar = statusBar;

        _checkTimer = new DispatcherTimer();
        _checkTimer.Tick += OnCheckTimerTick;
        _uploadTimer = new DispatcherTimer { Interval = UploadDelay };
        _uploadTimer.Tick += OnUploadTimerTick;
        _configuration.Saved += OnConfigurationSaved;
    }

    /// <summary>
    /// Raised after the list of versions in the database may have changed.
    /// </summary>
    public event EventHandler? VersionsChanged;

    public bool IsActive => _options.Enabled && _options.IsComplete;

    public Guid InstanceId => _options.InstanceId;

    public long LastKnownVersionId => _options.LastKnownVersionId;

    public SettingsSyncOptions GetOptions() => _options.Copy();

    /// <summary>
    /// Loads the connection and, when automatic checking is on, applies a newer version before devices connect.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            _options = await _optionsStore.LoadAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or CryptographicException)
        {
            UnhandledExceptionLogger.Log("Loading settings database connection", exception);
            StatusText = Localizer.Format("Sync_LoadFailed", exception.Message);
            return;
        }

        ApplySchedule();
        if (IsActive && _options.AutoCheck)
        {
            _statusBar.Message = Localizer.Text("Sync_Checking");
            await CheckForUpdatesAsync();
        }
    }

    /// <summary>
    /// Saves the connection settings. Pointing them at another database forgets the known version.
    /// </summary>
    public async Task<bool> SaveOptionsAsync(SettingsSyncOptions options)
    {
        await _gate.WaitAsync();
        try
        {
            var updated = options.Copy();
            updated.InstanceId = _options.InstanceId;
            updated.LastKnownVersionId = updated.HasSameTarget(_options) ? _options.LastKnownVersionId : 0;
            updated.CheckIntervalMinutes = Math.Clamp(updated.CheckIntervalMinutes, MinCheckIntervalMinutes, MaxCheckIntervalMinutes);
            updated.RetainedVersions = Math.Clamp(updated.RetainedVersions, MinRetainedVersions, MaxRetainedVersions);
            await _optionsStore.SaveAsync(updated);

            _options = updated;
            _store?.Dispose();
            _store = null;
            ApplySchedule();
            StatusText = Localizer.Text(IsActive ? "Sync_Saved" : "Sync_Disabled");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            UnhandledExceptionLogger.Log("Saving settings database connection", exception);
            StatusText = Localizer.Format("Sync_SaveFailed", exception.Message);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Connects, creates missing tables and verifies the encryption password. Returns the newest stored version.
    /// </summary>
    public async Task<(bool Connected, SettingsVersionInfo? Latest)> ConnectAsync()
    {
        SettingsVersionInfo? latest = null;
        var connected = await RunAsync(async store =>
        {
            await store.InitializeAsync();
            latest = await store.GetLatestAsync();
            StatusText = Localizer.Text("Sync_Connected");
        });

        return (connected, latest);
    }

    /// <summary>
    /// Sends the current configuration as a new version.
    /// </summary>
    public Task<bool> UploadAsync()
    {
        return RunAsync(UploadCoreAsync);
    }

    /// <summary>
    /// Sends changes still waiting for the automatic upload, e.g. when the application closes.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_uploadPending && IsActive)
        {
            await RunAsync(UploadCoreAsync);
        }
    }

    /// <summary>
    /// Applies the newest version when it was sent from another computer and is not known here yet.
    /// </summary>
    public Task<bool> CheckForUpdatesAsync()
    {
        return RunAsync(async store =>
        {
            if (_uploadPending)
            {
                // Local changes that have not been sent yet are newer than anything in the database.
                await UploadCoreAsync(store);
                return;
            }

            var latest = await store.GetLatestAsync();
            if (latest is null)
            {
                if (_options.AutoUpload)
                {
                    await UploadCoreAsync(store);
                }
                else
                {
                    StatusText = Localizer.Text("Sync_EmptyDatabase");
                }

                return;
            }

            if (latest.Id <= _options.LastKnownVersionId || latest.InstanceId == _options.InstanceId)
            {
                await RememberVersionAsync(Math.Max(latest.Id, _options.LastKnownVersionId));
                StatusText = Localizer.Format("Sync_UpToDate", DateTime.Now.ToString("t", Localizer.Instance.Culture));
                return;
            }

            await ApplyCoreAsync(store, latest, latest.Id);
            _statusBar.Message = StatusText;
        });
    }

    public Task<IReadOnlyList<SettingsVersionInfo>?> ListVersionsAsync()
    {
        return QueryAsync<IReadOnlyList<SettingsVersionInfo>>(async store => await store.ListAsync());
    }

    /// <summary>
    /// Replaces the configuration with the given version. With automatic sending an archived version
    /// is sent again as the newest one, so other computers receive it too.
    /// </summary>
    public Task<bool> ApplyVersionAsync(SettingsVersionInfo version)
    {
        return RunAsync(async store =>
        {
            var latest = await store.GetLatestAsync();
            var latestId = Math.Max(latest?.Id ?? version.Id, version.Id);
            await ApplyCoreAsync(store, version, latestId);
            if (version.Id != latestId && _options.AutoUpload)
            {
                var applied = StatusText;
                await UploadCoreAsync(store);
                StatusText = applied + " " + StatusText;
            }
        });
    }

    /// <summary>
    /// Treats the version as present here, so automatic checking does not apply it.
    /// </summary>
    public async Task MarkKnownAsync(long versionId)
    {
        await _gate.WaitAsync();
        try
        {
            await RememberVersionAsync(versionId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ReportError(exception, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _configuration.Saved -= OnConfigurationSaved;
        _checkTimer.Stop();
        _checkTimer.Tick -= OnCheckTimerTick;
        _uploadTimer.Stop();
        _uploadTimer.Tick -= OnUploadTimerTick;
        _store?.Dispose();
        _store = null;
    }

    private async Task UploadCoreAsync(IRemoteSettingsStore store)
    {
        _uploadTimer.Stop();
        var changeCount = _localChangeCount;
        var result = await store.UploadAsync(_configuration.Current.CreateSnapshot(), _options.InstanceId, _options.RetainedVersions);
        if (changeCount == _localChangeCount)
        {
            _uploadPending = false;
        }

        await RememberVersionAsync(result.Version.Id);
        StatusText = result.Created
            ? Localizer.Format("Sync_Uploaded", result.Version.Id)
            : Localizer.Format("Sync_UploadUnchanged", result.Version.Id);
        VersionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyCoreAsync(IRemoteSettingsStore store, SettingsVersionInfo version, long knownVersionId)
    {
        var configuration = await store.DownloadAsync(version.Id);
        _uploadTimer.Stop();
        _uploadPending = false;
        await _configuration.ReplaceAsync(configuration);
        await RememberVersionAsync(knownVersionId);
        StatusText = Localizer.Format("Sync_Applied", version.Id, version.MachineName,
            version.CreatedAt.ToLocalTime().ToString("g", Localizer.Instance.Culture));
        VersionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RememberVersionAsync(long versionId)
    {
        if (versionId == _options.LastKnownVersionId)
        {
            return;
        }

        _options.LastKnownVersionId = versionId;
        await _optionsStore.SaveAsync(_options.Copy());
    }

    private Task<bool> RunAsync(Func<IRemoteSettingsStore, Task> operation)
    {
        return RunCoreAsync(async store =>
        {
            await operation(store);
            return true;
        }, false);
    }

    private Task<T?> QueryAsync<T>(Func<IRemoteSettingsStore, Task<T?>> operation)
    {
        return RunCoreAsync(operation, default);
    }

    /// <summary>
    /// Runs one database operation at a time and reports failures in the status text.
    /// </summary>
    private async Task<T> RunCoreAsync<T>(Func<IRemoteSettingsStore, Task<T>> operation, T failed)
    {
        if (!IsActive)
        {
            StatusText = Localizer.Text("Sync_Disabled");
            return failed;
        }

        await _gate.WaitAsync();
        IsBusy = true;
        try
        {
            if (_isDisposed)
            {
                return failed;
            }

            _store ??= _storeFactory.Create(_options);
            return await operation(_store);
        }
        catch (SettingsSyncException exception)
        {
            ReportError(exception, Describe(exception));
            return failed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Saving the applied configuration or the known version on this computer failed.
            ReportError(exception, exception.Message);
            return failed;
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    private void ReportError(Exception exception, string message)
    {
        UnhandledExceptionLogger.Log("Settings database synchronization", exception);
        StatusText = Localizer.Format("Sync_Failed", message);
        _statusBar.Message = StatusText;
    }

    private static string Describe(SettingsSyncException exception)
    {
        return exception.Failure switch
        {
            SettingsSyncFailure.ConnectionFailed => Localizer.Format("Sync_Error_ConnectionFailed", exception.Message),
            SettingsSyncFailure.WrongEncryptionPassword => Localizer.Text("Sync_Error_WrongEncryptionPassword"),
            SettingsSyncFailure.CorruptedData => Localizer.Text("Sync_Error_CorruptedData"),
            SettingsSyncFailure.UnsupportedSchema => Localizer.Text("Sync_Error_UnsupportedSchema"),
            SettingsSyncFailure.VersionNotFound => Localizer.Text("Sync_Error_VersionNotFound"),
            _ => exception.Message
        };
    }

    private void ApplySchedule()
    {
        _checkTimer.Stop();
        if (IsActive && _options.AutoCheck)
        {
            _checkTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(_options.CheckIntervalMinutes, MinCheckIntervalMinutes, MaxCheckIntervalMinutes));
            _checkTimer.Start();
        }

        if (!IsActive || !_options.AutoUpload)
        {
            _uploadTimer.Stop();
            _uploadPending = false;
        }
    }

    private void OnConfigurationSaved(object? sender, EventArgs e)
    {
        if (_isDisposed || !IsActive || !_options.AutoUpload)
        {
            return;
        }

        // Settings pages save on every edit; send once the user pauses.
        _localChangeCount++;
        _uploadPending = true;
        _uploadTimer.Stop();
        _uploadTimer.Start();
    }

    private async void OnUploadTimerTick(object? sender, EventArgs e)
    {
        _uploadTimer.Stop();
        try
        {
            if (_uploadPending)
            {
                await RunAsync(UploadCoreAsync);
            }
        }
        catch (Exception exception)
        {
            UnhandledExceptionLogger.Log("Automatic settings upload", exception);
        }
    }

    private async void OnCheckTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await CheckForUpdatesAsync();
        }
        catch (Exception exception)
        {
            UnhandledExceptionLogger.Log("Automatic settings check", exception);
        }
    }
}
