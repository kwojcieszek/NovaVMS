namespace NovaVMS.Core.Models;

/// <summary>
/// Complete, persisted application configuration.
/// </summary>
public sealed class AppConfiguration
{
    public List<Device> Devices { get; set; } = new();

    public List<CameraGroup> Groups { get; set; } = new();

    public AppSettings Settings { get; set; } = new();
}
