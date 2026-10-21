namespace NovaVMS.Protocols.Common;

/// <summary>Drains a native stream's opening and stop before its callbacks/decoder may be released.</summary>
internal sealed class NativeStreamLifetime : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<int> _opening;
    private readonly TaskCompletionSource _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<int> _close;
    private readonly Action _complete;
    private readonly Action _cleanup;
    private Task? _closing;

    public NativeStreamLifetime(Func<CancellationToken, Task<int>> open, Action<int> close,
        Action complete, Action cleanup, bool startImmediately = true)
    {
        _close = close;
        _complete = complete;
        _cleanup = cleanup;
        _opening = Task.Run(async () =>
        {
            await _start.Task.ConfigureAwait(false);
            return _stop.IsCancellationRequested ? -1 : await open(_stop.Token).ConfigureAwait(false);
        });
        if (startImmediately) Start();
    }

    public void Start() => _start.TrySetResult();

    public void Dispose()
    {
        Task closing;
        lock (_gate)
        {
            // Release readers and blocked producer callbacks before waiting for native Stop.
            _closing ??= CloseAsync();
            closing = _closing;
        }
        closing.GetAwaiter().GetResult();
    }

    private async Task CloseAsync()
    {
        try
        {
            _complete();
            _stop.Cancel();
            _start.TrySetResult();
            var handle = await _opening.ConfigureAwait(false);
            if (handle >= 0) _close(handle);
        }
        finally
        {
            try { _cleanup(); }
            finally { _stop.Dispose(); }
        }
    }
}
