using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NovaVMS.App.Views;

/// <summary>
/// Modal yes / no question. Closing the window counts as "no".
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message)
        : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void OnYesClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnNoClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
