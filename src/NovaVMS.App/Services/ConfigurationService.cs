using NovaVMS.App.Models;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Core.Security;

namespace NovaVMS.App.Services;

/// <summary>
/// Current configuration held in memory together with operations on devices, groups and settings files.
/// </summary>
public sealed class ConfigurationService
{
    private readonly IConfigurationStore _store;
    private readonly IConfigurationSerializer _serializer;
    private readonly AsyncWorkQueue _saves = new();

    public ConfigurationService(IConfigurationStore store, IConfigurationSerializer serializer)
    {
        _store = store;
        _serializer = serializer;
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Raised after a local change has been saved; not raised when a configuration from the settings database is applied.
    /// </summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Raised after the whole configuration was replaced; objects taken from the previous one are no longer part of it.
    /// </summary>
    public event EventHandler? Replaced;

    public Task FlushAsync() => _saves.WhenIdle;

    public AppConfiguration Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        Current = Normalize(await Task.Run(() => _store.LoadAsync()));
        if (EnsureDefaultUser(Current))
        {
            // The default account is saved at once, so it keeps its identity and salt between starts.
            await SaveAsync();
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Name and password of the account created when the configuration has no users.
    /// </summary>
    public const string DefaultUserName = "admin";

    /// <summary>
    /// Adds the default administrator when no user exists, so the application can always be logged into.
    /// </summary>
    public static bool EnsureDefaultUser(AppConfiguration configuration)
    {
        if (configuration.Users.Count > 0)
        {
            return false;
        }

        var admin = new UserAccount { UserName = DefaultUserName, Permissions = UserPermissions.All };
        PasswordHasher.SetPassword(admin, DefaultUserName);
        configuration.Users.Add(admin);
        return true;
    }

    public UserAccount? FindUser(Guid userId)
    {
        return Current.Users.FirstOrDefault(user => user.Id == userId);
    }

    public UserAccount? FindUser(string userName)
    {
        return Current.Users.FirstOrDefault(user => string.Equals(user.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
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
        var replacement = Normalize(configuration);
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
            Users = Current.Users.Select(user => user.Copy()).ToList()
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
        return Current.Devices.FirstOrDefault(device => device.Id == deviceId);
    }

    public CameraGroup? FindGroup(Guid groupId)
    {
        return Current.Groups.FirstOrDefault(group => group.Id == groupId);
    }

    public CameraDescriptor? FindCamera(CameraReference reference)
    {
        var device = FindDevice(reference.DeviceId);
        if (device is null)
        {
            return null;
        }

        var channel = device.Channels.FirstOrDefault(candidate => candidate.Number == reference.ChannelNumber);
        if (channel is null)
        {
            return null;
        }

        return new CameraDescriptor(device, channel);
    }

    public IReadOnlyList<CameraDescriptor> GetAllCameras()
    {
        return Current.Devices
            .SelectMany(device => device.Channels.Select(channel => new CameraDescriptor(device, channel)))
            .ToList();
    }

    public CameraGroup CreateCustomGroup(string name)
    {
        var group = new CameraGroup { Name = name };
        Current.Groups.Add(group);
        return group;
    }

    public CameraGroup CreateGroupFromDevice(Device device)
    {
        var group = new CameraGroup
        {
            Name = device.DisplayName,
            SourceDeviceId = device.Id
        };

        foreach (var channel in device.Channels)
        {
            group.Cameras.Add(new CameraReference { DeviceId = device.Id, ChannelNumber = channel.Number });
        }

        Current.Groups.Add(group);
        return group;
    }

    /// <summary>
    /// Adds cameras that are not in the group yet. Returns the number of cameras added.
    /// </summary>
    public int AddCamerasToGroup(CameraGroup group, IEnumerable<CameraReference> cameras)
    {
        var added = 0;
        foreach (var camera in cameras)
        {
            if (!group.Cameras.Contains(camera))
            {
                group.Cameras.Add(camera);
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Appends new device channels to its group; optionally creates the group when no group contains the device cameras.
    /// </summary>
    public void SyncDeviceGroup(Device device, bool allowCreate)
    {
        var group = Current.Groups.FirstOrDefault(candidate => candidate.SourceDeviceId == device.Id);
        if (group is null)
        {
            var referenced = Current.Groups.Any(candidate => candidate.Cameras.Any(camera => camera.DeviceId == device.Id));
            if (allowCreate && Current.Settings.AutoCreateDeviceGroups && !referenced && device.Channels.Count > 0)
            {
                CreateGroupFromDevice(device);
            }

            return;
        }

        AddCamerasToGroup(group, device.Channels.Select(channel => new CameraReference { DeviceId = device.Id, ChannelNumber = channel.Number }));
    }

    public void RemoveDevice(Guid deviceId)
    {
        Current.Devices.RemoveAll(device => device.Id == deviceId);
        Current.Groups.RemoveAll(group => group.SourceDeviceId == deviceId);
        foreach (var group in Current.Groups)
        {
            group.Cameras.RemoveAll(camera => camera.DeviceId == deviceId);
        }
    }

    /// <summary>
    /// Writes the configuration to a stream. Passwords are cleared unless explicitly requested.
    /// </summary>
    public async Task ExportAsync(Stream stream, bool includePasswords)
    {
        var copy = Current.CreateSnapshot();
        if (!includePasswords)
        {
            foreach (var device in copy.Devices)
            {
                device.Password = string.Empty;
                device.StreamEncryptionKey = string.Empty;
            }

            foreach (var user in copy.Users)
            {
                user.PasswordHash = string.Empty;
                user.PasswordSalt = string.Empty;
                user.PasswordIterations = 0;
            }
        }

        await Task.Run(() => _serializer.SerializeAsync(stream, copy));
    }

    public async Task<AppConfiguration> ReadImportAsync(Stream stream)
    {
        return Normalize(await Task.Run(() => _serializer.DeserializeAsync(stream)));
    }

    /// <summary>
    /// Merges an imported configuration: items with the same ID are replaced, new ones are added
    /// and general settings are overwritten. Empty imported passwords keep the current password.
    /// </summary>
    public async Task<ImportResult> ImportAsync(AppConfiguration imported)
    {
        var devicesAdded = 0;
        var devicesUpdated = 0;
        foreach (var device in imported.Devices)
        {
            var index = Current.Devices.FindIndex(existing => existing.Id == device.Id);
            if (index < 0)
            {
                Current.Devices.Add(device);
                devicesAdded++;
                continue;
            }

            if (string.IsNullOrEmpty(device.Password))
            {
                device.Password = Current.Devices[index].Password;
            }
            if (string.IsNullOrEmpty(device.StreamEncryptionKey))
                device.StreamEncryptionKey = Current.Devices[index].StreamEncryptionKey;

            Current.Devices[index] = device;
            devicesUpdated++;
        }

        var groupsAdded = 0;
        var groupsUpdated = 0;
        foreach (var group in imported.Groups)
        {
            var index = Current.Groups.FindIndex(existing => existing.Id == group.Id);
            if (index < 0)
            {
                Current.Groups.Add(group);
                groupsAdded++;
            }
            else
            {
                Current.Groups[index] = group;
                groupsUpdated++;
            }
        }

        Current.Settings = imported.Settings;
        foreach (var view in imported.Views)
        {
            var index = Current.Views.FindIndex(existing => existing.Id == view.Id);
            if (index < 0) Current.Views.Add(view.Copy());
            else Current.Views[index] = view.Copy();
        }

        ImportUsers(imported.Users);
        await SaveAsync();

        return new ImportResult(devicesAdded, devicesUpdated, groupsAdded, groupsUpdated);
    }

    /// <summary>
    /// Users with the same ID are replaced; an export without passwords keeps the current password.
    /// New users without a password, or with a name already taken, are skipped so nobody gets an account without a password.
    /// </summary>
    private void ImportUsers(IEnumerable<UserAccount> users)
    {
        foreach (var user in users)
        {
            var index = Current.Users.FindIndex(existing => existing.Id == user.Id);
            var sameName = FindUser(user.UserName);
            if (index < 0)
            {
                if (user.HasPassword && sameName is null && !string.IsNullOrWhiteSpace(user.UserName))
                {
                    Current.Users.Add(user.Copy());
                }

                continue;
            }

            if (sameName is not null && sameName.Id != user.Id)
            {
                continue;
            }

            var updated = user.Copy();
            if (!updated.HasPassword)
            {
                updated.PasswordHash = Current.Users[index].PasswordHash;
                updated.PasswordSalt = Current.Users[index].PasswordSalt;
                updated.PasswordIterations = Current.Users[index].PasswordIterations;
            }

            Current.Users[index] = updated;
        }
    }

    // JSON files edited by hand may contain nulls for collections that the model declares as non-null.
    private static AppConfiguration Normalize(AppConfiguration configuration)
    {
        configuration.Devices ??= new List<Device>();
        configuration.Groups ??= new List<CameraGroup>();
        configuration.Settings ??= new AppSettings();
        configuration.GroupLiveViews ??= new();
        configuration.Views ??= new();
        configuration.Users ??= new();
        configuration.Users.RemoveAll(user => user is null);
        foreach (var user in configuration.Users)
        {
            user.UserName ??= string.Empty;
            user.PasswordHash ??= string.Empty;
            user.PasswordSalt ??= string.Empty;
        }

        foreach (var view in configuration.Views)
        {
            view.Layout ??= new();
            view.Layout.Cameras ??= new();
            view.Layout.EmptySlots ??= new();
        }

        foreach (var device in configuration.Devices)
        {
            device.SecondaryHost ??= string.Empty;
            device.StreamEncryptionKey ??= string.Empty;
            device.Channels ??= new List<Channel>();
            foreach (var channel in device.Channels)
            {
                // Older configurations may still hold stream numbers ("101") as channel names.
                channel.Name = ChannelNameNormalizer.Normalize(channel.Name ?? string.Empty, channel.Number);
            }
        }

        foreach (var group in configuration.Groups)
        {
            group.Cameras ??= new List<CameraReference>();
        }

        return configuration;
    }
}
