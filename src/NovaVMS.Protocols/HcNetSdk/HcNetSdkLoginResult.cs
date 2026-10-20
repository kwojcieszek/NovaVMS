using NovaVMS.Core.Exceptions;

namespace NovaVMS.Protocols.HcNetSdk;

internal sealed record HcNetSdkLoginResult(HcNetSdkSession? Session, DeviceCommunicationException? Error);
