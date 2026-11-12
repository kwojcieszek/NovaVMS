using Avalonia.Threading;
using NovaVMS.Infrastructure.Diagnostics;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Application log of the phone: failed operations, player messages and unhandled exceptions, written to
/// errors-yyyyMMdd.log files in the "logs" folder of the application data, in the desktop log format.
/// The files together keep at most <see cref="MaxBytes"/>; the oldest entries are removed first.
/// </summary>
public static class MobileLog
{
    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly object Gate = new();
    private static ExceptionLogWriter? _writer;
    private static int _installed;

    /// <summary>Folder with the log files; empty until <see cref="Install"/> runs.</summary>
    public static string LogDirectory { get; private set; } = string.Empty;

    public static void Install(string dataDirectory)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        LogDirectory = Path.Combine(dataDirectory, "logs");
        _writer = new ExceptionLogWriter(() => LogDirectory);

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Unobserved task exception", e.Exception);
        Dispatcher.UIThread.UnhandledException += (_, e) => Write("UI thread exception", e.Exception);
    }

    public static void Write(string source, Exception? exception = null)
    {
        if (_writer is not { } writer)
        {
            return;
        }

        lock (Gate)
        {
            writer.Write(source, exception);

            try
            {
                Trim(LogDirectory, MaxBytes);
            }
            catch (Exception trimFailure) when (trimFailure is IOException or UnauthorizedAccessException)
            {
                // The next entry trims again.
            }
        }
    }

    /// <summary>
    /// Removes the oldest log files and then the oldest entries of the newest file until all files together
    /// take at most <paramref name="maxBytes"/>.
    /// </summary>
    public static void Trim(string directory, int maxBytes)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        // The date in the name orders the files from the oldest.
        var files = new DirectoryInfo(directory).GetFiles("errors-????????.log").OrderBy(file => file.Name).ToList();
        var total = files.Sum(file => file.Length);

        while (files.Count > 1 && total > maxBytes)
        {
            total -= files[0].Length;
            files[0].Delete();
            files.RemoveAt(0);
        }

        if (files.Count == 0 || files[0].Length <= maxBytes)
        {
            return;
        }

        var bytes = File.ReadAllBytes(files[0].FullName);
        var cut = bytes.Length - maxBytes;
        var start = cut;

        // Keep whole entries: the kept part starts at the header of an entry, at the start of a line.
        while (start < bytes.Length && !(bytes[start] == (byte)'[' && bytes[start - 1] == (byte)'\n'))
        {
            start++;
        }

        // The newest entry alone is longer than the limit: keep its end from the start of a line.
        if (start == bytes.Length)
        {
            start = Array.IndexOf(bytes, (byte)'\n', cut) + 1;
            start = start == 0 ? bytes.Length : start;
        }

        File.WriteAllBytes(files[0].FullName, bytes[start..]);
    }

    /// <summary>Records a failed operation without making the caller wait for the file.</summary>
    public static void Post(string source, Exception? exception = null)
    {
        if (_writer is not null)
        {
            _ = Task.Run(() => Write(source, exception));
        }
    }

    /// <summary>RTSP address without the user name and password, for the log.</summary>
    public static string Describe(Uri uri)
    {
        return new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString();
    }

    public static Task ClearFileAsync(string path)
    {
        return _writer is { } writer ? Task.Run(() =>
        {
            lock (Gate)
            {
                writer.ClearFile(path);
            }
        }) : Task.CompletedTask;
    }
}
