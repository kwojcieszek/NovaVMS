using Avalonia;
using NovaVMS.App.Services;

namespace NovaVMS.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            UnhandledExceptionLogger.Install();
            // An installer holds this file exclusively until all replacement/rollback work is complete.
            var updateLock = Path.Combine(AppContext.BaseDirectory, ".novavms-update.lock");
            if (File.Exists(updateLock))
                using (new FileStream(updateLock, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            UnhandledExceptionLogger.LogStartupFailure(exception);
            Environment.ExitCode = 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
