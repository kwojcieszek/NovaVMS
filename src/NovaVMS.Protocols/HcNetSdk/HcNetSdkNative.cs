using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Functions of the Hikvision Device Network SDK (HCNetSDK). The library is not shipped with the application;
/// <see cref="HcNetSdkRuntime"/> loads it from the folder selected by the user.
/// Structures with a layout that differs between 32 and 64 bit are passed as raw buffers.
/// </summary>
internal static class HcNetSdkNative
{
    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_Init")]
    public static extern bool Init();

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_Cleanup")]
    public static extern bool Cleanup();

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_GetLastError")]
    public static extern uint GetLastError();

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_GetSDKBuildVersion")]
    public static extern uint GetSdkBuildVersion();

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_SetSDKInitCfg")]
    public static extern bool SetSdkInitConfig(int configType, IntPtr value);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_SetConnectTime")]
    public static extern bool SetConnectTime(uint waitTimeMs, uint tryTimes);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_SetReconnect")]
    public static extern bool SetReconnect(uint intervalMs, bool enable);

    /// <summary>
    /// Logs in; <paramref name="deviceInfo"/> receives NET_DVR_DEVICEINFO_V30. Returns the user id or -1.
    /// </summary>
    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_Login_V30", CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int Login(string address, ushort port, string userName, string password, IntPtr deviceInfo);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_Logout")]
    public static extern bool Logout(int userId);

    /// <summary>
    /// Starts live view; <paramref name="previewInfo"/> holds NET_DVR_PREVIEWINFO. Returns the stream handle or -1.
    /// </summary>
    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_RealPlay_V40")]
    public static extern int RealPlay(int userId, IntPtr previewInfo, HcNetSdkDataCallback callback, IntPtr user);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_StopRealPlay")]
    public static extern bool StopRealPlay(int realHandle);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_PlayBackByTime")]
    public static extern int PlayBackByTime(int userId, int channel, ref NetDvrTime start, ref NetDvrTime stop, IntPtr window);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_SetPlayDataCallBack_V40")]
    public static extern bool SetPlayDataCallback(int playHandle, HcNetSdkDataCallback callback, IntPtr user);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_PlayBackControl_V40")]
    public static extern bool PlayBackControl(int playHandle, uint command, IntPtr inBuffer, uint inLength, IntPtr outBuffer, ref uint outLength);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_StopPlayBack")]
    public static extern bool StopPlayBack(int playHandle);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_FindFile_V30")]
    public static extern int FindFile(int userId, ref NetDvrFileCondition condition);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_FindNextFile_V30")]
    public static extern int FindNextFile(int findHandle, out NetDvrFindData data);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_FindClose_V30")]
    public static extern bool FindClose(int findHandle);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_STDXMLConfig")]
    public static extern bool StdXmlConfig(int userId, ref NetDvrXmlConfigInput input, ref NetDvrXmlConfigOutput output);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_PTZControlWithSpeed_Other")]
    public static extern bool PtzControlWithSpeed(int userId, int channel, uint command, uint stop, uint speed);

    [DllImport(HcNetSdkConstants.LibraryName, EntryPoint = "NET_DVR_CaptureJPEGPicture_NEW")]
    public static extern bool CaptureJpegPicture(int userId, int channel, ref NetDvrJpegParameters parameters, byte[] buffer, uint bufferSize, ref uint returnedSize);
}
