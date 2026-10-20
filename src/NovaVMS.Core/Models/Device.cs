using System.Text.Json.Serialization;

namespace NovaVMS.Core.Models;

/// <summary>
/// Device stored in the application configuration.
/// </summary>
public sealed class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int HttpPort { get; set; } = 80;

    public int RtspPort { get; set; } = 554;

    /// <summary>
    /// Port of the Hikvision device network protocol (HCNetSDK).
    /// </summary>
    public int SdkPort { get; set; } = 8000;

    public bool UseHttps { get; set; }

    public string Username { get; set; } = "admin";

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Optional Hikvision stream decryption key supplied by the user, separate from the login password.
    /// Empty leaves the original, unencrypted playback path unchanged.
    /// </summary>
    public string StreamEncryptionKey { get; set; } = string.Empty;

    public DeviceType Type { get; set; } = DeviceType.Unknown;

    public DeviceProtocol Protocol { get; set; } = DeviceProtocol.Isapi;

    /// <summary>
    /// Model, serial number and firmware read during the last successful synchronization.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = string.Empty;

    public List<Channel> Channels { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}
