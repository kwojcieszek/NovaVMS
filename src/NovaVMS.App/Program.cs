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
