using Avalonia.Threading;
using NovaVMS.App.Localization;

namespace NovaVMS.App.Services;

/// <summary>Logs recoverable operation failures and publishes a status message on the UI thread.</summary>
internal static class DeviceOperationErrors
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> Recent = new();
    public static event Action<string>? ErrorReported;

    public static string Report(string operation, Exception exception)
    {
        // Reconnecting tiles can repeat the same failure every few seconds.
        var signature = operation + "|" + exception.GetType().FullName + "|" + exception.Message;
        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if (!Recent.TryGetValue(signature, out var previous) || now - previous >= TimeSpan.FromSeconds(30))
            {
                if (Recent.Count >= 4096) Recent.Clear();
                Recent[signature] = now;
                UnhandledExceptionLogger.Log(operation, exception);
            }
        }
        var message = exception is OperationCanceledException ? Localizer.Text("DeviceOperation_Timeout") : exception.Message;
        if (ErrorReported is not null)
        {
            if (Dispatcher.UIThread.CheckAccess()) ErrorReported?.Invoke(message);
            else Dispatcher.UIThread.Post(() => ErrorReported?.Invoke(message));
        }
        return message;
    }
}
