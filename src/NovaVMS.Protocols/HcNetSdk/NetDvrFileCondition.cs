using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

// NET_DVR_FILECOND and NET_DVR_FINDDATA_V30 use fixed-width DWORD/LONG
// fields on both 32-bit and 64-bit SDKs. Times are local to the recorder.
[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrFileCondition
{
    public int Channel;
    public uint FileType;
    public uint IsLocked;
    public uint UseCardNumber;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] CardNumber;
    public NetDvrTime Start;
    public NetDvrTime End;
}
