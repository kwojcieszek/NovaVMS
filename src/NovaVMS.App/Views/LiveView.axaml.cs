using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NovaVMS.App.Views;

public partial class LiveView : UserControl
{
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

        window.WindowState = window.WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }
}
