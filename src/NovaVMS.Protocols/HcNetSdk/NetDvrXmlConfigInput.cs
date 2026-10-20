using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// NET_DVR_XML_CONFIG_INPUT - ISAPI request sent through the SDK connection.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrXmlConfigInput
{
    public uint Size;

    /// <summary>
    /// Request line, e.g. "GET /ISAPI/System/deviceInfo\r\n".
    /// </summary>
    public IntPtr RequestUrl;

    public uint RequestUrlLength;

    public IntPtr InBuffer;

    public uint InBufferSize;

    public uint ReceiveTimeoutMs;

    public byte ForceEncryption;

    public byte NumberOfMultiParts;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 30)]
    public byte[] Reserved;
}
