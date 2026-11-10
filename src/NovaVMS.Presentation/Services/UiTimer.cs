namespace NovaVMS.Presentation.Services;

/// <summary>
/// Timer raising <see cref="Tick"/> on the thread that started it (the interface thread), like the dispatcher timer
/// of the interface framework, which the shared view models cannot use: the applications use different versions of it.
/// A tick that was already queued when the timer was stopped or started again is dropped.
/// </summary>
public sealed class UiTimer : IDisposable
{
    private readonly ITimer _timer;
    private SynchronizationContext? _context;
    private int _generation;
    private bool _isRunning;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public event EventHandler? Tick;

    public UiTimer(TimeProvider? timeProvider = null)
    {
        _timer = (timeProvider ?? TimeProvider.System).CreateTimer(OnElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        _context = SynchronizationContext.Current ?? _context;
        _isRunning = true;

        Interlocked.Increment(ref _generation);

        _timer.Change(Interval, Interval);
    }

    public void Stop()
    {
        _isRunning = false;

        Interlocked.Increment(ref _generation);

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        Stop();

        _timer.Dispose();
    }

    // Called on the thread pool.
    private void OnElapsed(object? state)
    {
        var generation = Volatile.Read(ref _generation);

        if (_context is { } context)
        {
            context.Post(_ => Raise(generation), null);

            return;
        }

        Raise(generation);
    }

    private void Raise(int generation)
    {
        if (_isRunning && generation == Volatile.Read(ref _generation))
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}
