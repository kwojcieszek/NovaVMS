using Avalonia.Threading;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Core.Exceptions;
using NovaVMS.Infrastructure.Diagnostics;
using System.Threading.Channels;

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
    [ThreadStatic] private static bool _writingLog;
    private sealed record Entry(string Source, Exception? Exception, TaskCompletionSource? Barrier = null);
    private static readonly Channel<Entry> Entries = Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions
    {
        SingleReader = true, AllowSynchronousContinuations = false
    });
    private static readonly Task LogWorker = Task.Run(WriteLogsAsync);

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
        // FirstChanceException runs on the throwing thread, including the UI thread.
        // Never wait there for disk access or a logger lock held by another thread.
        if (_writingLog) return;
        Entries.Writer.TryWrite(new Entry($"{source} [origin thread {Environment.CurrentManagedThreadId}]", exception));
    }

    internal static Task FlushAsync()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Entries.Writer.TryWrite(new Entry("", null, barrier));
        return barrier.Task;
    }

    internal static void LogStartupFailure(Exception exception) => WriteLog("Application startup or main loop failure", exception);

    private static async Task WriteLogsAsync()
    {
        await foreach (var entry in Entries.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (entry.Barrier is not null) { entry.Barrier.TrySetResult(); continue; }
            WriteLog(entry.Source, entry.Exception);
        }
    }

    private static void WriteLog(string source, Exception? exception)
    {
        // Ignore first-chance exceptions from a failed log write; otherwise a bad
        // log directory could feed the queue recursively forever.
        _writingLog = true;
        try { Writer.Write(source, exception); }
        finally { _writingLog = false; }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Fatal process termination cannot wait for the asynchronous log pump.
        WriteLog(e.IsTerminating ? "Fatal unhandled exception" : "Unhandled exception", e.ExceptionObject as Exception);
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
