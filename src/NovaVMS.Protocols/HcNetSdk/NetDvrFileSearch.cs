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

internal interface IHcNetSdkRecordingApi
{
    int Find(int userId, ref NetDvrFileCondition condition);
    int Next(int handle, out NetDvrFindData data);
    bool Close(int handle);
    uint GetLastError();
}

internal sealed class HcNetSdkRecordingApi : IHcNetSdkRecordingApi
{
    public int Find(int userId, ref NetDvrFileCondition condition) => HcNetSdkNative.FindFile(userId, ref condition);
    public int Next(int handle, out NetDvrFindData data) => HcNetSdkNative.FindNextFile(handle, out data);
    public bool Close(int handle) => HcNetSdkNative.FindClose(handle);
    public uint GetLastError() => HcNetSdkNative.GetLastError();
}
