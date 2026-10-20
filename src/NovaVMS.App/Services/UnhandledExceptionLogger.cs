using Avalonia.Threading;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Core.Exceptions;

namespace NovaVMS.App.Services;

/// <summary>
/// Writes unhandled exceptions to a daily log file in the application data directory,
/// so failures (also those that happen while the application closes) can be diagnosed.
/// </summary>
internal static class UnhandledExceptionLogger
{
    private static readonly object FileLock = new();

    /// <summary>
    /// Set while the application is closing; UI-thread exceptions are then logged and suppressed.
    /// </summary>
    public static bool IsShuttingDown { get; set; }

    public static string LogDirectory => AppDataPaths.LogDirectory;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void AttachToDispatcher()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    public static void Log(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"errors-{DateTime.Now:yyyyMMdd}.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";

            lock (FileLock)
            {
                File.AppendAllText(path, entry);
            }
        }
        catch (IOException)
        {
            // Logging must never take the application down.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging must never take the application down.
        }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log("Unhandled exception", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        foreach (var exception in e.Exception.Flatten().InnerExceptions)
        {
            if (DeviceFailure.IsExpected(exception)) DeviceOperationErrors.Report("Background device operation", exception);
            else Log("Unobserved task exception", exception);
        }
        e.SetObserved();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (!IsShuttingDown && DeviceFailure.IsExpected(e.Exception))
        {
            DeviceOperationErrors.Report("Recovered UI device operation", e.Exception);
            e.Handled = true;
            return;
        }
        Log(IsShuttingDown ? "UI thread exception during shutdown" : "UI thread exception", e.Exception);
        if (IsShuttingDown)
        {
            e.Handled = true;
        }
    }
}
