namespace NovaVMS.App.Models;

/// <summary>
/// Result of the periodic reachability check shown in the device list.
/// </summary>
public enum DeviceConnectionState
{
    Unknown,
    Checking,
    Online,
    Offline
}
