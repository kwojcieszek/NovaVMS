namespace NovaVMS.Core.Models;

/// <summary>
/// Protocol used to control the device.
/// </summary>
public enum DeviceProtocol
{
    Isapi,
    Onvif,

    /// <summary>
    /// Hikvision device network protocol (port 8000) through the vendor's HCNetSDK library.
    /// </summary>
    HikvisionSdk
}
