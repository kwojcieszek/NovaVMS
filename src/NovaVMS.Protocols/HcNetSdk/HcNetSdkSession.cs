using System.Runtime.InteropServices;
using System.Text;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Logged-in SDK connection to a device with the channel layout read at login (NET_DVR_DEVICEINFO_V30).
/// </summary>
internal sealed class HcNetSdkSession
{
    // Offsets in NET_DVR_DEVICEINFO_V30.
    private const int SerialNumberLength = 48;
    private const int AnalogChannelCountOffset = 52;
    private const int StartChannelOffset = 53;
    private const int IpChannelCountOffset = 55;
    private const int StartDigitalChannelOffset = 66;
    private const int IpChannelCountHighOffset = 68;

    /// <summary>
    /// Size reserved for NET_DVR_DEVICEINFO_V30 (80 bytes) with a safety margin.
    /// </summary>
    public const int DeviceInfoBufferSize = 256;

    public HcNetSdkSession(int userId, string host, int port, IntPtr deviceInfo)
    {
        UserId = userId;
        Host = host;
        Port = port;

        var serial = new byte[SerialNumberLength];
        Marshal.Copy(deviceInfo, serial, 0, serial.Length);
        SerialNumber = Encoding.ASCII.GetString(serial).TrimEnd('\0', ' ');

        AnalogChannelCount = Marshal.ReadByte(deviceInfo, AnalogChannelCountOffset);
        StartChannel = Math.Max(1, (int)Marshal.ReadByte(deviceInfo, StartChannelOffset));
        IpChannelCount = Marshal.ReadByte(deviceInfo, IpChannelCountOffset) + (Marshal.ReadByte(deviceInfo, IpChannelCountHighOffset) << 8);
        StartDigitalChannel = Marshal.ReadByte(deviceInfo, StartDigitalChannelOffset);
    }

    public int UserId { get; }

    public string Host { get; }

    public int Port { get; }

    public string SerialNumber { get; }

    public int AnalogChannelCount { get; }

    public int StartChannel { get; }

    public int IpChannelCount { get; }

    public int StartDigitalChannel { get; }

    public int ChannelCount => AnalogChannelCount + IpChannelCount;

    /// <summary>
    /// Converts the channel number used by ISAPI and the interface (analog inputs first, then IP channels,
    /// numbered from 1) to the SDK channel number (IP channels usually start at 33).
    /// </summary>
    public int ToSdkChannel(int channelNumber)
    {
        if (channelNumber <= AnalogChannelCount || IpChannelCount == 0 || StartDigitalChannel == 0)
        {
            return StartChannel + channelNumber - 1;
        }

        return StartDigitalChannel + channelNumber - AnalogChannelCount - 1;
    }
}
