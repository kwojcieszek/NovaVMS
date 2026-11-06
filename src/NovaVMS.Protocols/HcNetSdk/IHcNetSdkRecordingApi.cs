namespace NovaVMS.Protocols.HcNetSdk;

internal interface IHcNetSdkRecordingApi
{
    int Find(int userId, ref NetDvrFileCondition condition);
    int Next(int handle, out NetDvrFindData data);
    bool Close(int handle);
    uint GetLastError();
}
