namespace NovaVMS.App.Services;

/// <summary>Cancels new work and waits for in-flight calls, including non-cancellable native calls.</summary>
internal sealed class OperationLifetime
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _stopping;

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        lock (_gate)
        {
            if (_stopping) return Task.FromCanceled<T>(new CancellationToken(true));
            _active++;
        }
        return RunCoreAsync(operation, token);
    }

    private async Task<T> RunCoreAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            return await operation(linked.Token);
        }
        finally
        {
            lock (_gate)
            {
                if (--_active == 0 && _stopping) _drained.TrySetResult();
            }
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopping) return _drained.Task;
            _stopping = true;
        }
        // Cancellation callbacks may finish operations: never invoke them while holding the gate.
        _stop.Cancel();
        lock (_gate)
        {
            if (_active == 0) _drained.TrySetResult();
            return _drained.Task;
        }
    }
}
