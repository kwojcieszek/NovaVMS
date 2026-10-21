using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Import;

namespace NovaVMS.App.Services;

internal static class ImportedCameraGroups
{
    public static string DeviceKey(Device device)
    {
        var port = device.Protocol == DeviceProtocol.HikvisionSdk ? device.SdkPort : device.HttpPort;
        return $"{device.Protocol}:{device.Host.Trim()}:{port}";
    }

    public static int Merge(AppConfiguration configuration, DeviceCsvResult import)
    {
        var devices = configuration.Devices.GroupBy(DeviceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var remapped = new Dictionary<Guid, Device>();
        foreach (var source in import.Devices)
            if (devices.TryGetValue(DeviceKey(source), out var target)) remapped[source.Id] = target;
        var processed = 0;
        foreach (var source in import.Groups)
        {
            var target = configuration.Groups.FirstOrDefault(group => group.IsCustom
                && string.Equals(group.Name, source.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                target = new CameraGroup { Name = source.Name };
                configuration.Groups.Add(target);
            }
            foreach (var camera in source.Cameras)
            {
                if (!remapped.TryGetValue(camera.DeviceId, out var device)) continue;
                // Existing offline devices may not have discovered their channels yet.
                if (!device.Channels.Any(channel => channel.Number == camera.ChannelNumber))
                {
                    var imported = import.Devices.First(candidate => candidate.Id == camera.DeviceId)
                        .Channels.FirstOrDefault(channel => channel.Number == camera.ChannelNumber);
                    if (imported is null) continue;
                    device.Channels.Add(new Channel { Number = imported.Number, Name = imported.Name, Enabled = imported.Enabled });
                }
                var reference = new CameraReference { DeviceId = device.Id, ChannelNumber = camera.ChannelNumber };
                if (!target.Cameras.Contains(reference)) target.Cameras.Add(reference);
            }
            processed++;
        }
        return processed;
    }
}
