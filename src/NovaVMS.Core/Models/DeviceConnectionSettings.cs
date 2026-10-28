namespace NovaVMS.Core.Models;

/// <summary>Independent optional connection, shared with the device configuration.</summary>
public sealed class DeviceConnectionSettings
{
    public DeviceProtocol Protocol { get; set; } = DeviceProtocol.Isapi;
    public string Host { get; set; } = string.Empty;
    public string SecondaryHost { get; set; } = string.Empty;
    public int? SecondaryPort { get; set; }
    public int? SecondaryRtspPort { get; set; }
    public int HttpPort { get; set; } = 80;
    public int RtspPort { get; set; } = 554;
    public int SdkPort { get; set; } = 8000;
    public bool UseHttps { get; set; }
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = string.Empty;
    public string StreamEncryptionKey { get; set; } = string.Empty;
    public List<Channel> Channels { get; set; } = new();

    public DeviceConnectionSettings Copy()
    {
        var copy = (DeviceConnectionSettings)MemberwiseClone();
        copy.Channels = Channels.Select(channel => channel.Copy()).ToList();
        return copy;
    }
}
