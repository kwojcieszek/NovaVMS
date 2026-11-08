using NovaVMS.App.Models;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Configuration;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Current configuration held in memory together with operations on devices, groups and settings files.
/// </summary>
public sealed class ConfigurationService
{
    /// <summary>
    /// Name and password of the account created when the configuration has no users.
    /// </summary>
    public const string DefaultUserName = AppConfigurationNormalizer.DefaultUserName;

    private readonly IConfigurationStore _store;
    private readonly IConfigurationSerializer _serializer;
    private readonly AsyncWorkQueue _saves = new();

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

    public ConfigurationService(IConfigurationStore store, IConfigurationSerializer serializer)
    {
        _store = store;
        _serializer = serializer;
    }

    /// <summary>
    /// Adds the default administrator when no user exists, so the application can always be logged into.
    /// </summary>
    public static bool EnsureDefaultUser(AppConfiguration configuration)
    {
        return AppConfigurationNormalizer.EnsureDefaultUser(configuration);
    }

    public Task FlushAsync() => _saves.WhenIdle;

    public async Task LoadAsync()
    {
        Current = AppConfigurationNormalizer.Normalize(await Task.Run(() => _store.LoadAsync()));

        if (EnsureDefaultUser(Current))
        {
            // The default account is saved at once, so it keeps its identity and salt between starts.
            await SaveAsync();

            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public UserAccount? FindUser(Guid userId)
    {
        return Current.FindUser(userId);
    }

    public UserAccount? FindUser(string userName)
    {
        return Current.FindUser(userName);
    }

    public async Task SaveAsync()
    {
        // Capture a stable graph on the UI thread; serialization, encryption and disk
        // access run in FIFO order off-thread even when the backing store completes synchronously.
        var snapshot = Current.CreateSnapshot();

        await _saves.RunAsync(() => _store.SaveAsync(snapshot));
        Changed?.Invoke(this, EventArgs.Empty);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces the whole configuration with one received from the settings database and saves it locally.
    /// The SDK folder stays a setting of this computer.
    /// </summary>
    public async Task ReplaceAsync(AppConfiguration configuration)
    {
        var replacement = AppConfigurationNormalizer.Normalize(configuration);
        replacement.Settings.HcNetSdkDirectory = Current.Settings.HcNetSdkDirectory;

        // A version sent before users existed must not lock everyone out.
        if (replacement.Users.Count == 0)
        {
            replacement.Users = Current.Users.Select(user => user.Copy()).ToList();

            EnsureDefaultUser(replacement);
        }

        Current = replacement;

        var snapshot = Current.CreateSnapshot();

        await _saves.RunAsync(() => _store.SaveAsync(snapshot));
        Replaced?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces imported resources, retaining this application's general settings.</summary>
    public async Task ReplaceDevicesAndGroupsAsync(IReadOnlyList<Device> devices, IReadOnlyList<CameraGroup> groups)
    {
        var replacement = new AppConfiguration
        {
            Settings = Current.CreateSnapshot().Settings,
            Devices = devices.ToList(),
            Groups = groups.ToList(),
            Users = Current.Users.Select(user => user.Copy()).ToList(),
            // Views stay; their cameras show again when the imported devices contain them.
            Views = Current.Views.Select(view => view.Copy()).ToList()
        }.CreateSnapshot();
        // A failed write must leave the current devices and groups available.
        await _saves.RunAsync(() => _store.SaveAsync(replacement.CreateSnapshot()));

        Current = replacement;

        Replaced?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    public Device? FindDevice(Guid deviceId)
    {
        return Current.FindDevice(deviceId);
    }

    public CameraGroup? FindGroup(Guid groupId)
    {
        return Current.FindGroup(groupId);
    }

    public CameraDescriptor? FindCamera(CameraReference reference)
    {
        return Current.TryFindCamera(reference, out var device, out var channel) ? new CameraDescriptor(device, channel) : null;
    }

    public IReadOnlyList<CameraDescriptor> GetAllCameras()
    {
        return Current.Devices
            .SelectMany(device => device.Channels.Select(channel => new CameraDescriptor(device, channel)))
            .ToList();
    }

    public CameraGroup CreateCustomGroup(string name)
    {
        return Current.CreateCustomGroup(name);
    }

    public CameraGroup CreateGroupFromDevice(Device device)
    {
        return Current.CreateGroupFromDevice(device);
    }

    /// <summary>
    /// Adds cameras that are not in the group yet. Returns the number of cameras added.
    /// </summary>
    public int AddCamerasToGroup(CameraGroup group, IEnumerable<CameraReference> cameras)
    {
        return AppConfigurationExtensions.AddCamerasToGroup(group, cameras);
    }

    /// <summary>
    /// Appends new device channels to its group; optionally creates the group when no group contains the device cameras.
    /// </summary>
    public void SyncDeviceGroup(Device device, bool allowCreate)
    {
        Current.SyncDeviceGroup(device, allowCreate);
    }

    public void RemoveDevice(Guid deviceId)
    {
        Current.RemoveDevice(deviceId);
    }

    /// <summary>
    /// Writes the configuration to a stream. Passwords are cleared unless explicitly requested.
    /// </summary>
    public async Task ExportAsync(Stream stream, bool includePasswords)
    {
        var copy = Current.CreateSnapshot();

        if (!includePasswords)
        {
            AppConfigurationTransfer.RemoveSecrets(copy);
        }

        await Task.Run(() => _serializer.SerializeAsync(stream, copy));
    }

    public async Task<AppConfiguration> ReadImportAsync(Stream stream)
    {
        return AppConfigurationNormalizer.Normalize(await Task.Run(() => _serializer.DeserializeAsync(stream)));
    }

    /// <summary>
    /// Merges an imported configuration: items with the same ID are replaced, new ones are added
    /// and general settings are overwritten. Empty imported passwords keep the current password.
    /// </summary>
    public async Task<ImportResult> ImportAsync(AppConfiguration imported)
    {
        var result = AppConfigurationTransfer.Merge(Current, imported);

        await SaveAsync();

        return new ImportResult(result.DevicesAdded, result.DevicesUpdated, result.GroupsAdded, result.GroupsUpdated);
    }
}
