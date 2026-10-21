using Avalonia.Controls;

namespace NovaVMS.App.Views;

/// <summary>
/// Shown while the application stops streams and releases devices and native resources before it closes.
/// </summary>
public partial class ShutdownWindow : Window
{
    public ShutdownWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The window cannot be closed by the user; the shutdown closes it when it is done.
    /// </summary>
    public bool CanClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = !CanClose;
        base.OnClosing(e);
    }
}
