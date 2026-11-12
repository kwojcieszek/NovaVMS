using System.Runtime.InteropServices;
using NovaVMS.Core.Localization;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>Loads the vendor playback library from the selected SDK directory, with the process architecture.</summary>
internal sealed class PlayCtrlApi : IPlayCtrlApi
{
    private static readonly Lazy<PlayCtrlApi> Instance = new(Load);

    private readonly GetPortCall _getPort;
    private readonly ModeCall _mode;
    private readonly OpenCall _open;
    private readonly KeyCall _key;
    private readonly CallbackCall _callback;
    private readonly PlayCall _play;
    private readonly InputCall _input;
    private readonly PortCall _stop, _close, _free;
    private readonly ErrorCall _error;

    private PlayCtrlApi(IntPtr library)
    {
        // Delegates and the library stay loaded for the lifetime of the process, including late native callbacks.
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        _getPort = Export<GetPortCall>("PlayM4_GetPort");
        _mode = Export<ModeCall>("PlayM4_SetStreamOpenMode");
        _open = Export<OpenCall>("PlayM4_OpenStream");
        _key = Export<KeyCall>("PlayM4_SetSecretKey");
        _callback = Export<CallbackCall>("PlayM4_SetDecCallBackExMend");
        _play = Export<PlayCall>("PlayM4_Play");
        _input = Export<InputCall>("PlayM4_InputData");
        _stop = Export<PortCall>("PlayM4_Stop");
        _close = Export<PortCall>("PlayM4_CloseStream");
        _free = Export<PortCall>("PlayM4_FreePort");
        _error = Export<ErrorCall>("PlayM4_GetLastError");
    }

    public static IPlayCtrlApi Get() => Instance.Value;

    public bool GetPort(out int port) => _getPort(out port);

    public bool SetMode(int port, uint mode) => _mode(port, mode);

    public bool OpenStream(int port, byte[] header, uint size, uint capacity) => _open(port, header, size, capacity);

    public bool SetKey(int port, int type, byte[] key, int lengthBits) => _key(port, type, key, lengthBits);

    public bool SetCallback(int port, PlayCtrlDecodeCallback callback, IntPtr destination, int size, IntPtr user) => _callback(port, callback, destination, size, user);

    public bool Play(int port, IntPtr window) => _play(port, window);

    public bool Input(int port, byte[] bytes, uint size) => _input(port, bytes, size);

    public bool Stop(int port) => _stop(port);

    public bool Close(int port) => _close(port);

    public bool Free(int port) => _free(port);

    public uint Error(int port) => _error(port);

    private static PlayCtrlApi Load()
    {
        HcNetSdkRuntime.EnsureInitialized();

        var name = OperatingSystem.IsWindows() ? "PlayCtrl.dll" : "libPlayCtrl.so";
        var path = Path.Combine(HcNetSdkRuntime.LoadedDirectory!, name);

        try
        {
            return new PlayCtrlApi(NativeLibrary.Load(path));
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new NotSupportedException(LibraryText.Get("StreamDecoderUnavailable"), exception);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool PortCall(int port);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool GetPortCall(out int port);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool ModeCall(int port, uint mode);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool OpenCall(int port, byte[] header, uint size, uint capacity);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool KeyCall(int port, int type, byte[] key, int lengthBits);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool CallbackCall(int port, PlayCtrlDecodeCallback callback, IntPtr destination, int size, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool PlayCall(int port, IntPtr window);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool InputCall(int port, byte[] data, uint size);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint ErrorCall(int port);
}
