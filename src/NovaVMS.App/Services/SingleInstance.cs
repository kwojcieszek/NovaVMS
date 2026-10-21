using System.IO.Pipes;

namespace NovaVMS.App.Services;

/// <summary>
/// Allows one running application per computer. A second start asks the running one to show its window and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    // "Global\" makes the lock shared by all user sessions on Windows; other systems ignore the prefix.
    private const string MutexName = @"Global\NovaVMS.SingleInstance";
    private const string PipeName = "NovaVMS.Activate";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// The lock of this process, while it is the running instance.
    /// </summary>
    public static SingleInstance? Current { get; private set; }

    /// <summary>
    /// Takes the lock, or returns null when another instance holds it.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, MutexName);
        }
        catch (UnauthorizedAccessException)
        {
            // The lock exists in another user's session.
            return null;
        }

        try
        {
            if (!mutex.WaitOne(TimeSpan.Zero))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous instance ended without releasing the lock; it now belongs to this process.
        }

        return Current = new SingleInstance(mutex);
    }

    /// <summary>
    /// Asks the running instance to show its main window. Failures are ignored: the running instance may be closing.
    /// </summary>
    public static void ActivateRunningInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(ConnectTimeout);
            pipe.WriteByte(1);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Calls <paramref name="activate"/> (on a background thread) whenever another start of the application is attempted.
    /// </summary>
    public void ListenForActivation(Action activate)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    var buffer = new byte[1];
                    if (await pipe.ReadAsync(buffer, _stop.Token).ConfigureAwait(false) > 0)
                    {
                        activate();
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException exception)
                {
                    UnhandledExceptionLogger.Log("Single instance activation", exception);
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by the calling thread.
        }

        _mutex.Dispose();
        Current = null;
    }
}
