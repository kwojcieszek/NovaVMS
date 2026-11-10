using Android.Views;
using NovaVMS.Mobile.Views;

namespace NovaVMS.Mobile.Android;

/// <summary>
/// Keeps the start screen of the window (the system start screen on Android 12 and later) until the shell has read
/// the configuration: the first frame of the window is held back meanwhile. A shell that does not start within
/// <see cref="MaximumHold"/> is shown anyway.
/// </summary>
public sealed class StartScreenHold : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
{
    private static readonly TimeSpan MaximumHold = TimeSpan.FromSeconds(5);

    private readonly View _content;
    private readonly MobileShell _shell;
    private readonly DateTime _until;
    private bool _released;

    public StartScreenHold(View content, MobileShell shell)
    {
        _content = content;
        _shell = shell;
        _until = DateTime.UtcNow + MaximumHold;

        _content.ViewTreeObserver?.AddOnPreDrawListener(this);
    }

    // False skips the frame and the window tries again with the next one.
    public bool OnPreDraw()
    {
        if (_released)
        {
            return true;
        }

        if (!_shell.IsStarted && DateTime.UtcNow < _until)
        {
            return false;
        }

        _released = true;

        if (_content.ViewTreeObserver is { IsAlive: true } observer)
        {
            observer.RemoveOnPreDrawListener(this);
        }

        return true;
    }
}
