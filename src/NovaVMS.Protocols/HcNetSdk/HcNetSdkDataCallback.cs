using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Stream data callback of live view (REALDATACALLBACK) and playback (PLAYDATACALLBACK_V40).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void HcNetSdkDataCallback(int handle, uint dataType, IntPtr buffer, uint bufferSize, IntPtr user);
