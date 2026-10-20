using System.Net;
using NovaVMS.Core.Exceptions;
using NovaVMS.Protocols.Resources;

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
                return new DeviceCommunicationException(ProtocolText.Get("InvalidCredentials"), HttpStatusCode.Unauthorized);
            }
            case HcNetSdkConstants.ErrorUserLocked:
            {
                return new DeviceCommunicationException(ProtocolText.Get("SdkUserLocked"), HttpStatusCode.Forbidden);
            }
            case HcNetSdkConstants.ErrorNetworkConnect:
            case HcNetSdkConstants.ErrorNetworkSend:
            case HcNetSdkConstants.ErrorNetworkReceive:
            {
                return new DeviceCommunicationException(ProtocolText.Format("SdkConnectFailed", host, port));
            }
            case HcNetSdkConstants.ErrorNetworkTimeout:
            {
                return new DeviceCommunicationException(ProtocolText.Get("Timeout"));
            }
            case HcNetSdkConstants.ErrorChannel:
            {
                return new DeviceCommunicationException(ProtocolText.Format("SdkChannelOffline", code));
            }
            case HcNetSdkConstants.ErrorNotSupported:
            {
                return new DeviceCommunicationException(ProtocolText.Format("FunctionNotSupported", code), HttpStatusCode.NotFound);
            }
            default:
            {
                return new DeviceCommunicationException(ProtocolText.Format("SdkError", code));
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
