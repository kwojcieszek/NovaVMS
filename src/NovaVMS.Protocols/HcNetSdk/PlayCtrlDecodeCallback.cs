using System.Runtime.InteropServices;

namespace NovaVMS.Protocols.HcNetSdk;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void PlayCtrlDecodeCallback(int port, IntPtr buffer, int size, ref PlayCtrlFrameInfo info, IntPtr user, int reserved);
