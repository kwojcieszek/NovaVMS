using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Configuration;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Services;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Current configuration held in memory, in the same format as the desktop application, so it can be
/// exchanged through the settings database. Saves run one at a time. The phone has no user login:
/// desktop user accounts are kept unchanged and are never created or edited here.
/// </summary>
public sealed class MobileConfigurationService : ISynchronizedConfiguration
{
    private readonly IConfigurationStore _store;
    private readonly IConfigurationSerializer _serializer;
    private readonly MobileDeviceStore _legacyStore;
    private readonly SemaphoreSlim _saves = new(1, 1);

    public AppConfiguration Current { get; private set; } = new();

    public event EventHandler? Changed;

    /// <summary>
    /// Raised after a local change has been saved; not raised when a configuration from the settings database is applied.
    /// </summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Raised after the whole configuration was replaced; objects taken from the previous one are no longer part of it.
    /// </summary>
    public event EventHandler? Replaced;

    public MobileConfigurationService(IConfigurationStore store, IConfigurationSerializer serializer, MobileDeviceStore legacyStore)
    {
        _store = store;
        _serializer = serializer;
        _legacyStore = legacyStore;
    }

    /// <summary>Loads the configuration; devices saved by earlier mobile versions are moved into it once.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Current = AppConfigurationNormalizer.Normalize(await _store.LoadAsync(cancellationToken));

        var migrated = false;

        if (_legacyStore.Exists)
        {
            var devices = await _legacyStore.LoadAsync(cancellationToken);

            foreach (var device in devices.Where(device => Current.FindDevice(device.Id) is null))
            {
                Current.Devices.Add(device);
            }

            Current = AppConfigurationNormalizer.Normalize(Current);

            // Earlier versions listed cameras by device; device groups keep them reachable from the main page.
            foreach (var device in Current.Devices)
            {
                Current.SyncDeviceGroup(device, allowCreate: true);
            }

            migrated = true;
        }

        if (migrated)
        {
            await SaveAsync(cancellationToken);

            _legacyStore.Delete();

            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = Current.CreateSnapshot();

        await WriteAsync(snapshot, cancellationToken);

        Changed?.Invoke(this, EventArgs.Empty);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces the whole configuration with one received from the settings database and saves it locally.
    /// The SDK folder stays a setting of the computer that set it.
    /// </summary>
    public async Task ReplaceAsync(AppConfiguration configuration)
    {
        // The phone has no user login; desktop accounts are only carried along, so a version without
        // users keeps the ones already known.
        Current = AppConfigurationTransfer.PrepareReplacement(Current, configuration);

        await WriteAsync(Current.CreateSnapshot(), CancellationToken.None);

        Replaced?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the configuration to a stream in the desktop export format. Passwords are cleared unless explicitly requested.
    /// </summary>
    public async Task ExportAsync(Stream stream, bool includePasswords)
    {
        await AppConfigurationTransfer.ExportAsync(_serializer, stream, Current, includePasswords);
    }

    public Task<AppConfiguration> ReadImportAsync(Stream stream)
    {
        return AppConfigurationTransfer.ReadAsync(_serializer, stream);
    }

    /// <summary>
    /// Merges an imported configuration exactly like the desktop application and saves it.
    /// </summary>
    public async Task<AppConfigurationImportResult> ImportAsync(AppConfiguration imported)
    {
        var result = AppConfigurationTransfer.Merge(Current, imported);

        await SaveAsync();

        return result;
    }

    private async Task WriteAsync(AppConfiguration snapshot, CancellationToken cancellationToken)
    {
        await _saves.WaitAsync(cancellationToken);

        try
        {
            await Task.Run(() => _store.SaveAsync(snapshot, cancellationToken), cancellationToken);
        }
        finally
        {
            _saves.Release();
        }
    }
}
