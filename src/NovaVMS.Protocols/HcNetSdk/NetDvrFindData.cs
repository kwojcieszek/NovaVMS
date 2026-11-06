using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrFindData
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
    public byte[] FileName;
    public NetDvrTime Start;
    public NetDvrTime End;
    public uint FileSize;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] CardNumber;
    public byte Locked;
    public byte FileType;
    public ushort Reserved;
}
