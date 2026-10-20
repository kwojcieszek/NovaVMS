using System.Text;

namespace NovaVMS.Infrastructure.Diagnostics;

/// <summary>Synchronously persists exceptions, including during process termination.</summary>
public sealed class ExceptionLogWriter(Func<string> logDirectory, Func<string>? fallbackDirectory = null)
{
    private readonly object _fileLock = new();
    [ThreadStatic] private static bool _isWriting;

    public void Write(string source, Exception? exception)
    {
        // A failing file operation also raises FirstChanceException. Never log the logger recursively.
        if (_isWriting) return;
        _isWriting = true;
        try
        {
            var now = DateTimeOffset.Now;
            string details;
            try { details = exception?.ToString() ?? string.Empty; }
            catch { details = exception?.GetType().FullName ?? "Exception details unavailable"; }
            var entry = $"[{now:yyyy-MM-dd HH:mm:ss.fff zzz}] [PID {Environment.ProcessId}, thread {Environment.CurrentManagedThreadId}] {source}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}";
            var fileName = $"errors-{now:yyyyMMdd}.log";
            lock (_fileLock)
            {
                if (TryWrite(logDirectory, fileName, entry)) return;
                if (fallbackDirectory is not null && TryWrite(fallbackDirectory, fileName, entry)) return;
            }
            try { Console.Error.Write(entry); } catch { }
        }
        catch
        {
            // Exception reporting must never replace the original failure with a logging failure.
        }
        finally { _isWriting = false; }
    }

    private static bool TryWrite(Func<string> directoryProvider, string fileName, string entry)
    {
        try
        {
            var directory = directoryProvider();
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, fileName), entry, new UTF8Encoding(false));
            return true;
        }
        catch { return false; }
    }
}
