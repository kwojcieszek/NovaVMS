namespace NovaVMS.Protocols.HcNetSdk;

internal interface IPlayCtrlApi
{
    bool GetPort(out int port);
    bool SetMode(int port, uint mode);
    bool OpenStream(int port, byte[] header, uint size, uint capacity);
    bool SetKey(int port, int type, byte[] key, int lengthBits);
    bool SetCallback(int port, PlayCtrlDecodeCallback callback, IntPtr destination, int size, IntPtr user);
    bool Play(int port, IntPtr window);
    bool Input(int port, byte[] bytes, uint size);
    bool Stop(int port);
    bool Close(int port);
    bool Free(int port);
    uint Error(int port);
}
