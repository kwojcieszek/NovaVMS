using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

[StructLayout(LayoutKind.Sequential)]
internal struct PlayCtrlFrameInfo
{
    public int Width, Height, Timestamp, Type, FrameRate;
    public uint FrameNumber;
}
