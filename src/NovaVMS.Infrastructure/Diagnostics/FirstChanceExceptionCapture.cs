using System.Runtime.ExceptionServices;

namespace NovaVMS.Infrastructure.Diagnostics;

/// <summary>Records managed exceptions when thrown, even if a later catch handles them.</summary>
public sealed class FirstChanceExceptionCapture(Action<string, Exception> log) : IDisposable
{
    private readonly object _gate = new();
    private bool _installed;
    [ThreadStatic] private static bool _isHandling;

    public void Install()
    {
        lock (_gate)
        {
            if (_installed) return;
            AppDomain.CurrentDomain.FirstChanceException += OnException;
            _installed = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_installed) return;
            AppDomain.CurrentDomain.FirstChanceException -= OnException;
            _installed = false;
        }
    }

    private void OnException(object? sender, FirstChanceExceptionEventArgs e)
    {
        if (_isHandling) return;
        _isHandling = true;
        try { log("First-chance exception", e.Exception); }
        catch { }
        finally { _isHandling = false; }
    }
}
