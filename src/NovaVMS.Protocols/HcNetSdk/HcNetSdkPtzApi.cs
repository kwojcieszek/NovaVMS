namespace NovaVMS.Protocols.HcNetSdk;

internal sealed class HcNetSdkPtzApi : IHcNetSdkPtzApi
{
    public bool Control(int userId, int channel, uint command, uint stop, uint speed)
        => HcNetSdkNative.PtzControlWithSpeed(userId, channel, command, stop, speed);
    public uint GetLastError() => HcNetSdkNative.GetLastError();
}
