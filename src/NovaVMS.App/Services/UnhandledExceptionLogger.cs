using Avalonia.Threading;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Core.Exceptions;
using NovaVMS.Infrastructure.Diagnostics;

namespace NovaVMS.App.Services;

/// <summary>
/// Logs managed exceptions at the point of throwing, plus UI, task and fatal exception context.
/// </summary>
internal static class UnhandledExceptionLogger
{
    private static readonly ExceptionLogWriter Writer = new(
        () => AppDataPaths.LogDirectory,
        () => Path.Combine(Path.GetTempPath(), "NovaVMS", "logs"));
    private static readonly FirstChanceExceptionCapture FirstChance = new(Log);
    private static int _installed;
    private static int _dispatcherAttached;

    /// <summary>
    /// Set while the application is closing; UI-thread exceptions are then logged and suppressed.
    /// </summary>
    public static bool IsShuttingDown { get; set; }

    public static string LogDirectory => AppDataPaths.LogDirectory;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        FirstChance.Install();
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void AttachToDispatcher()
    {
        if (Interlocked.Exchange(ref _dispatcherAttached, 1) != 0) return;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    public static void Log(string source, Exception? exception)
    {
        Writer.Write(source, exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log(e.IsTerminating ? "Fatal unhandled exception" : "Unhandled exception", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log("Unobserved task exception", e.Exception);
        try
        {
            foreach (var exception in e.Exception.Flatten().InnerExceptions)
                if (DeviceFailure.IsExpected(exception)) DeviceOperationErrors.Report("Background device operation", exception);
        }
        catch (Exception exception) { Log("Background exception notification failed", exception); }
        finally { e.SetObserved(); }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(IsShuttingDown ? "UI thread exception during shutdown" : "UI thread exception", e.Exception);
        if (!IsShuttingDown && DeviceFailure.IsExpected(e.Exception))
        {
            try { DeviceOperationErrors.Report("Recovered UI device operation", e.Exception); }
            catch (Exception exception) { Log("UI exception notification failed", exception); }
            e.Handled = true;
            return;
        }
        if (IsShuttingDown)
        {
            e.Handled = true;
        }
    }
}
