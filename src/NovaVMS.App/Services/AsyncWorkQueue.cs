namespace NovaVMS.App.Services;

/// <summary>Runs blocking native operations off the UI thread, in order, including cleanup.</summary>
internal sealed class AsyncWorkQueue
{
    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;

    public Task WhenIdle { get { lock (_sync) return _tail; } }

    public Task<T> RunAsync<T>(Func<T> operation) => RunCoreAsync(() => Task.FromResult(operation()));

    public Task RunAsync(Func<Task> operation) => RunCoreAsync(async () =>
    {
        await operation().ConfigureAwait(false);
        return true;
    });

    private Task<T> RunCoreAsync<T>(Func<Task<T>> operation)
    {
        lock (_sync)
        {
            var previous = _tail;
            var task = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                return await operation().ConfigureAwait(false);
            });
            // A failed operation must not prevent a later Stop/Dispose from running.
            _tail = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted) UnhandledExceptionLogger.Log("Background native operation", completed.Exception);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    public Task RunAsync(Action operation) => RunAsync(() => { operation(); return true; });
}
