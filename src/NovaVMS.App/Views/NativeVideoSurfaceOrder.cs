using System.Runtime.InteropServices;

namespace NovaVMS.App.Views;

internal static class NativeVideoSurfaceOrder
{
    // Avalonia 11 wraps each HWND in a sibling clipping/positioning window.
    // Reorder those holders, not VLC's child HWNDs (which have different parents).
    internal static bool ShowInFront(IntPtr front, IntPtr back)
    {
        if (!OperatingSystem.IsWindows() || front == IntPtr.Zero || back == IntPtr.Zero)
            return false;
        var frontHolder = GetParent(front);
        var backHolder = GetParent(back);
        if (frontHolder == IntPtr.Zero || backHolder == IntPtr.Zero || frontHolder == backHolder
            || GetParent(frontHolder) == IntPtr.Zero || GetParent(frontHolder) != GetParent(backHolder))
            return false;

        // Put only the other surface behind the selected one. Do not move either
        // surface, change visibility, activate a window or disturb other tiles.
        return SetWindowPos(backHolder, frontHolder, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);
}
