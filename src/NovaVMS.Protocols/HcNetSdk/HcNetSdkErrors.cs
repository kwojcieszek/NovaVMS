using System.Net;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Localization;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Turns HCNetSDK error codes into exceptions with a readable message.
/// </summary>
internal static class HcNetSdkErrors
{
    public static DeviceCommunicationException LastError(string host, int port)
    {
        return FromCode(HcNetSdkNative.GetLastError(), host, port);
    }

    public static DeviceCommunicationException FromCode(uint code, string host, int port)
    {
        switch (code)
        {
            case HcNetSdkConstants.ErrorPassword:
                {
                    return new DeviceCommunicationException(LibraryText.Get("InvalidCredentials"), HttpStatusCode.Unauthorized);
                }
            case HcNetSdkConstants.ErrorUserLocked:
                {
                    return new DeviceCommunicationException(LibraryText.Get("SdkUserLocked"), HttpStatusCode.Forbidden);
                }
            case HcNetSdkConstants.ErrorNetworkConnect:
            case HcNetSdkConstants.ErrorNetworkSend:
            case HcNetSdkConstants.ErrorNetworkReceive:
                {
                    return new DeviceCommunicationException(LibraryText.Format("SdkConnectFailed", host, port));
                }
            case HcNetSdkConstants.ErrorNetworkTimeout:
                {
                    return new DeviceCommunicationException(LibraryText.Get("Timeout"));
                }
            case HcNetSdkConstants.ErrorChannel:
                {
                    return new DeviceCommunicationException(LibraryText.Format("SdkChannelOffline", code));
                }
            case HcNetSdkConstants.ErrorNotSupported:
                {
                    return new DeviceCommunicationException(LibraryText.Format("FunctionNotSupported", code), HttpStatusCode.NotFound);
                }
            default:
                {
                    return new DeviceCommunicationException(LibraryText.Format("SdkError", code));
                }
        }
    }

    /// <summary>
    /// Errors after which the login session is no longer usable and a new login is needed.
    /// </summary>
    public static bool InvalidatesSession(uint code)
    {
        return code is HcNetSdkConstants.ErrorPassword
            or HcNetSdkConstants.ErrorNetworkConnect
            or HcNetSdkConstants.ErrorNetworkSend
            or HcNetSdkConstants.ErrorNetworkReceive
            or HcNetSdkConstants.ErrorNetworkTimeout;
    }
}
