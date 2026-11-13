namespace NovaVMS.Protocols.HcNetSdk;

internal sealed class HcNetSdkRecordingApi : IHcNetSdkRecordingApi
{
    public int Find(int userId, ref NetDvrFileCondition condition)
    {
        return HcNetSdkNative.FindFile(userId, ref condition);
    }

    public int Next(int handle, out NetDvrFindData data)
    {
        return HcNetSdkNative.FindNextFile(handle, out data);
    }

    public bool Close(int handle)
    {
        return HcNetSdkNative.FindClose(handle);
    }

    public uint GetLastError()
    {
        return HcNetSdkNative.GetLastError();
    }
}
