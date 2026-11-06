namespace NovaVMS.Protocols.HcNetSdk;

internal sealed class HcNetSdkRecordingApi : IHcNetSdkRecordingApi
{
    public int Find(int userId, ref NetDvrFileCondition condition) => HcNetSdkNative.FindFile(userId, ref condition);
    public int Next(int handle, out NetDvrFindData data) => HcNetSdkNative.FindNextFile(handle, out data);
    public bool Close(int handle) => HcNetSdkNative.FindClose(handle);
    public uint GetLastError() => HcNetSdkNative.GetLastError();
}
