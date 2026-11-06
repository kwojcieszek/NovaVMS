namespace NovaVMS.Protocols.HcNetSdk;

internal interface IHcNetSdkPtzApi
{
    bool Control(int userId, int channel, uint command, uint stop, uint speed);
    uint GetLastError();
}
