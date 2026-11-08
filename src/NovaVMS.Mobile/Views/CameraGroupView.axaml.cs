using Avalonia.Controls;
using NovaVMS.Mobile.ViewModels;

namespace NovaVMS.Mobile.Views;

public partial class CameraGroupView : UserControl
{
    public CameraGroupView()
    {
        InitializeComponent();
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
