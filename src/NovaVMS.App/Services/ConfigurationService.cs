using NovaVMS.App.Models;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Current configuration held in memory together with operations on devices, groups and settings files.
/// </summary>
public sealed class ConfigurationService
{
    private readonly IConfigurationStore _store;
    private readonly IConfigurationSerializer _serializer;

    public ConfigurationService(IConfigurationStore store, IConfigurationSerializer serializer)
    {
        _store = store;
        _serializer = serializer;
    }

    public event EventHandler? Changed;

    public AppConfiguration Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        Current = Normalize(await _store.LoadAsync());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync()
    {
        await _store.SaveAsync(Current);
        Changed?.Invoke(this, EventArgs.Empty);
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
        var copy = await CloneAsync(Current);
        if (!includePasswords)
        {
            foreach (var device in copy.Devices)
            {
                device.Password = string.Empty;
                device.StreamEncryptionKey = string.Empty;
            }
        }

        await _serializer.SerializeAsync(stream, copy);
    }

    public async Task<AppConfiguration> ReadImportAsync(Stream stream)
    {
        return Normalize(await _serializer.DeserializeAsync(stream));
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
        await SaveAsync();

        return new ImportResult(devicesAdded, devicesUpdated, groupsAdded, groupsUpdated);
    }

    private async Task<AppConfiguration> CloneAsync(AppConfiguration configuration)
    {
        using var buffer = new MemoryStream();
        await _serializer.SerializeAsync(buffer, configuration);
        buffer.Position = 0;
        return Normalize(await _serializer.DeserializeAsync(buffer));
    }

    // JSON files edited by hand may contain nulls for collections that the model declares as non-null.
    private static AppConfiguration Normalize(AppConfiguration configuration)
    {
        configuration.Devices ??= new List<Device>();
        configuration.Groups ??= new List<CameraGroup>();
        configuration.Settings ??= new AppSettings();

        foreach (var device in configuration.Devices)
        {
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
