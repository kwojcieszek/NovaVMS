using Avalonia;
using NovaVMS.App.Services;

namespace NovaVMS.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        UnhandledExceptionLogger.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
