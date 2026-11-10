using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NovaVMS.Mobile.ViewModels;

namespace NovaVMS.Mobile.Views;

public partial class CameraGroupView : UserControl
{
    private static readonly TimeSpan DoubleTapTime = TimeSpan.FromMilliseconds(450);

    private object? _lastTile;
    private DateTime _lastTapTime;

    public CameraGroupView()
    {
        InitializeComponent();
    }

    // A double tap on a preview opens its camera full screen; a single tap, e.g. while scrolling the list, does nothing.
    // The camera opens after the touch has ended, as its preview view is removed under the finger.
    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CameraTileViewModel tile })
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (!ReferenceEquals(tile, _lastTile) || now - _lastTapTime > DoubleTapTime)
        {
            _lastTile = tile;
            _lastTapTime = now;

            return;
        }

        _lastTile = null;

        Dispatcher.UIThread.Post(() =>
        {
            if (tile.OpenCommand.CanExecute(null))
            {
                tile.OpenCommand.Execute(null);
            }
        }, DispatcherPriority.Background);
    }

    private void OnColumnsDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is CameraGroupViewModel group)
        {
            _ = group.PausePreviewsAsync();
        }
    }

    private void OnColumnsDropDownClosed(object? sender, EventArgs e)
    {
        if (DataContext is CameraGroupViewModel group)
        {
            _ = group.ResumePreviewsAsync();
        }
    }

    // A touch selection leaves the list open; the previews start again when it closes.
    private void OnColumnsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: true } comboBox)
        {
            comboBox.IsDropDownOpen = false;
        }
    }
}
