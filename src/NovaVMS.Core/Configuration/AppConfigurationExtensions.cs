using NovaVMS.Core.Models;

namespace NovaVMS.Core.Configuration;

/// <summary>Lookups and group operations shared by the desktop and mobile applications.</summary>
public static class AppConfigurationExtensions
{
    public static Device? FindDevice(this AppConfiguration configuration, Guid deviceId)
    {
        return configuration.Devices.FirstOrDefault(device => device.Id == deviceId);
    }

    public static CameraGroup? FindGroup(this AppConfiguration configuration, Guid groupId)
    {
        return configuration.Groups.FirstOrDefault(group => group.Id == groupId);
    }

    public static UserAccount? FindUser(this AppConfiguration configuration, Guid userId)
    {
        return configuration.Users.FirstOrDefault(user => user.Id == userId);
    }

    public static UserAccount? FindUser(this AppConfiguration configuration, string userName)
    {
        return configuration.Users.FirstOrDefault(user => string.Equals(user.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds the device and channel a reference points to; legacy references use the channel number.</summary>
    public static bool TryFindCamera(this AppConfiguration configuration, CameraReference reference, out Device device, out Channel channel)
    {
        device = null!;
        channel = null!;

        var found = configuration.FindDevice(reference.DeviceId);

        if (found is null)
        {
            return false;
        }

        var foundChannel = reference.IsLegacy
            ? found.Channels.FirstOrDefault(candidate => candidate.Number == reference.ChannelNumber)
            : found.Channels.FirstOrDefault(candidate => candidate.Id == reference.ChannelId);

        if (foundChannel is null)
        {
            return false;
        }

        device = found;
        channel = foundChannel;

        return true;
    }

    public static CameraGroup CreateCustomGroup(this AppConfiguration configuration, string name)
    {
        var group = new CameraGroup { Name = name };

        configuration.Groups.Add(group);

        return group;
    }

    public static CameraGroup CreateGroupFromDevice(this AppConfiguration configuration, Device device)
    {
        var group = new CameraGroup
        {
            Name = device.DisplayName,
            SourceDeviceId = device.Id
        };

        foreach (var channel in device.Channels)
        {
            group.Cameras.Add(CameraReference.For(device, channel));
        }

        configuration.Groups.Add(group);

        return group;
    }

    /// <summary>
    /// Adds cameras that are not in the group yet. Returns the number of cameras added.
    /// </summary>
    public static int AddCamerasToGroup(CameraGroup group, IEnumerable<CameraReference> cameras)
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
    public static void SyncDeviceGroup(this AppConfiguration configuration, Device device, bool allowCreate)
    {
        var group = configuration.Groups.FirstOrDefault(candidate => candidate.SourceDeviceId == device.Id);

        if (group is null)
        {
            var referenced = configuration.Groups.Any(candidate => candidate.Cameras.Any(camera => camera.DeviceId == device.Id));

            if (allowCreate && configuration.Settings.AutoCreateDeviceGroups && !referenced && device.Channels.Count > 0)
            {
                configuration.CreateGroupFromDevice(device);
            }

            return;
        }

        AddCamerasToGroup(group, device.Channels.Select(channel => CameraReference.For(device, channel)));
    }

    public static void RemoveDevice(this AppConfiguration configuration, Guid deviceId)
    {
        configuration.Devices.RemoveAll(device => device.Id == deviceId);
        configuration.Groups.RemoveAll(group => group.SourceDeviceId == deviceId);

        foreach (var group in configuration.Groups)
        {
            group.Cameras.RemoveAll(camera => camera.DeviceId == deviceId);
        }
    }
}
