using System.Runtime.InteropServices;
using NovaVMS.App.Localization;

namespace NovaVMS.App.Services;

internal static class FatalErrorNotice
{
    public static void Show()
    {
        try
        {
            var message = Localizer.Format("Error_FatalMessage", UnhandledExceptionLogger.LogDirectory,
                Path.Combine(Path.GetTempPath(), "NovaVMS", "logs"));
            // The dispatcher may itself have failed, or Avalonia may not yet be initialized.
            // Use an OS dialog so a WinExe never silently swallows a startup/main-loop failure.
            if (OperatingSystem.IsWindows())
                MessageBox(IntPtr.Zero, message, Localizer.Text("Error_FatalTitle"), 0x10 | 0x10000);
            else
                Console.Error.WriteLine(message);
        }
        catch { /* Reporting a fatal error must not throw another exception. */ }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
