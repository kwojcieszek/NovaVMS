using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// NET_DVR_XML_CONFIG_OUTPUT - response of an ISAPI request sent through the SDK connection.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrXmlConfigOutput
{
    public uint Size;

    public IntPtr OutBuffer;

    public uint OutBufferSize;

    public uint ReturnedSize;

    /// <summary>
    /// ResponseStatus XML describing a failure.
    /// </summary>
    public IntPtr StatusBuffer;

    public uint StatusBufferSize;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] Reserved;
}
