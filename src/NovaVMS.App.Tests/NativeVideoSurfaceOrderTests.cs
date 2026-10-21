using System.Runtime.InteropServices;
using NovaVMS.App.Views;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class NativeVideoSurfaceOrderTests
{
    [Fact]
    public void HandoverReordersHoldersInBothDirectionsWithoutResizingOrHiding()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateWindowExW(0, "STATIC", "", 0, 0, 0, 640, 480, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, root);
        try
        {
            var firstHolder = Child(root);
            var secondHolder = Child(root);
            var first = Child(firstHolder);
            var second = Child(secondHolder);
            Assert.True(GetWindowRect(firstHolder, out var firstBounds));
            Assert.True(GetWindowRect(secondHolder, out var secondBounds));

            // Repeated SUB/MAIN/reverse handovers must preserve both live surfaces.
            for (var i = 0; i < 10; i++)
            {
                Assert.True(NativeVideoSurfaceOrder.ShowInFront(first, second));
                Assert.Equal(secondHolder, GetWindow(firstHolder, 2)); // GW_HWNDNEXT
                Assert.True(NativeVideoSurfaceOrder.ShowInFront(second, first));
                Assert.Equal(firstHolder, GetWindow(secondHolder, 2));
            }

            Assert.True(GetWindowRect(firstHolder, out var finalFirst));
            Assert.True(GetWindowRect(secondHolder, out var finalSecond));
            Assert.Equal(firstBounds, finalFirst);
            Assert.Equal(secondBounds, finalSecond);
            Assert.NotEqual(0, GetWindowLongW(firstHolder, -16) & 0x10000000);
            Assert.NotEqual(0, GetWindowLongW(secondHolder, -16) & 0x10000000);
            Assert.False(NativeVideoSurfaceOrder.ShowInFront(first, first));
            Assert.False(NativeVideoSurfaceOrder.ShowInFront(IntPtr.Zero, second));
        }
        finally
        {
            DestroyWindow(root);
        }
    }

    private static IntPtr Child(IntPtr parent)
    {
        var child = CreateWindowExW(0, "STATIC", "", 0x40000000 | 0x10000000,
            0, 0, 320, 240, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, child);
        return child;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string title,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowLongW(IntPtr window, int index);
}
