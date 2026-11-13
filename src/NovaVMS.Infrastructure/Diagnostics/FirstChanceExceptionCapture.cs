using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace NovaVMS.Infrastructure.Diagnostics;

/// <summary>Records managed exceptions when thrown, even if a later catch handles them.</summary>
public sealed class FirstChanceExceptionCapture : IDisposable
{
    [ThreadStatic] private static bool _isHandling;

    private readonly Action<string, Exception> _log;
    private readonly object _gate = new();
    private bool _installed;
    private readonly ConditionalWeakTable<Exception, Captured> _seen = new();

    public FirstChanceExceptionCapture(Action<string, Exception> log)
    {
        _log = log;
    }

    public void Install()
    {
        lock (_gate)
        {
            if (_installed)
            {
                return;
            }

            AppDomain.CurrentDomain.FirstChanceException += OnException;

            _installed = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_installed)
            {
                return;
            }

            AppDomain.CurrentDomain.FirstChanceException -= OnException;

            _installed = false;
        }
    }

    private void OnException(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Cancellation is routine when tiles close, a stream changes or a TCP probe times out.
        // Unhandled cancellations still reach the UI/domain/task exception handlers.
        if (_isHandling || e.Exception is OperationCanceledException)
        {
            return;
        }

        _isHandling = true;

        try
        {
            var captured = _seen.GetValue(e.Exception, _ => new Captured());

            if (Interlocked.Exchange(ref captured.Value, 1) != 0)
            {
                return;
            }

            _log("First-chance exception", e.Exception);
        }
        catch
        {
        }
        finally
        {
            _isHandling = false;
        }
    }

    private sealed class Captured
    {
        public int Value;
    }
}
