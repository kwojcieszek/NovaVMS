using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NovaVMS.App.Views;

public partial class LiveView : UserControl
{
    private WindowState _lastWindowState = WindowState.Maximized;

    public LiveView()
    {
        InitializeComponent();
    }

    private void OnFullScreenClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        if(window.WindowState != WindowState.FullScreen)
        {
            _lastWindowState = window.WindowState;
        }

        window.WindowState = window.WindowState == WindowState.FullScreen
            ? _lastWindowState
            : WindowState.FullScreen;
    }
}
