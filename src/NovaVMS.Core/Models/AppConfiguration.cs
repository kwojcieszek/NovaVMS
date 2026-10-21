namespace NovaVMS.Core.Models;

/// <summary>
/// Complete, persisted application configuration.
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>Copies mutable settings before serialization runs on a background thread.</summary>
    public AppConfiguration CreateSnapshot() => new()
    {
        Settings = Settings.Copy(),
        GroupLiveViews = GroupLiveViews.ToDictionary(pair => pair.Key, pair => pair.Value.Copy()),
        Devices = Devices.Select(device =>
        {
            var copy = device.ForHost(device.Host);
            copy.Channels = device.Channels.Select(channel => channel.Copy()).ToList();
            return copy;
        }).ToList(),
        Groups = Groups.Select(group => new CameraGroup
        {
            Id = group.Id, Name = group.Name, SourceDeviceId = group.SourceDeviceId,
            Cameras = group.Cameras.ToList() // CameraReference is immutable.
        }).ToList()
    };
    public List<Device> Devices { get; set; } = new();

    public List<CameraGroup> Groups { get; set; } = new();

    public AppSettings Settings { get; set; } = new();
    public Dictionary<string, GroupLiveView> GroupLiveViews { get; set; } = new();
}
