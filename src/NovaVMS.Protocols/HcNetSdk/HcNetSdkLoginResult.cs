using NovaVMS.Core.Exceptions;

namespace NovaVMS.Protocols.HcNetSdk;

internal sealed record HcNetSdkLoginResult
{
    public HcNetSdkSession? Session { get; init; }

    public DeviceCommunicationException? Error { get; init; }

    public HcNetSdkLoginResult(HcNetSdkSession? session, DeviceCommunicationException? error)
    {
        Session = session;
        Error = error;
    }
}
