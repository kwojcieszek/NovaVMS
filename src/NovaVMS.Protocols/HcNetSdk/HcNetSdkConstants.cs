namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Values of the HCNetSDK interface used by this client (names follow the SDK header).
/// </summary>
internal static class HcNetSdkConstants
{
    public const string LibraryName = "HCNetSDK";

    // NET_SDK_INIT_CFG_TYPE
    public const int InitConfigSdkPath = 2;
    public const int InitConfigCryptoLibraryPath = 3;
    public const int InitConfigSslLibraryPath = 4;

    // Stream data types passed to the data callbacks.
    public const uint DataSystemHeader = 1;
    public const uint DataStream = 2;

    // NET_DVR_PlayBackControl_V40 commands.
    public const uint PlaybackStart = 1;

    // Error codes (NET_DVR_GetLastError).
    public const uint ErrorPassword = 1;
    public const uint ErrorNetworkConnect = 7;
    public const uint ErrorNetworkSend = 8;
    public const uint ErrorNetworkReceive = 9;
    public const uint ErrorNetworkTimeout = 10;
    public const uint ErrorChannel = 4;
    public const uint ErrorNotSupported = 23;
    public const uint ErrorUserLocked = 153;

    // PTZ commands of NET_DVR_PTZControlWithSpeed_Other.
    public const uint PtzZoomIn = 11;
    public const uint PtzZoomOut = 12;
    public const uint PtzTiltUp = 21;
    public const uint PtzTiltDown = 22;
    public const uint PtzPanLeft = 23;
    public const uint PtzPanRight = 24;
    public const uint PtzUpLeft = 25;
    public const uint PtzUpRight = 26;
    public const uint PtzDownLeft = 27;
    public const uint PtzDownRight = 28;

    public const uint PtzMinSpeed = 1;
    public const uint PtzMaxSpeed = 7;
}
