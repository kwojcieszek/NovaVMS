using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NovaVMS.App.Views;

/// <summary>
/// Modal dialog asking for a single line of text. Returns the text, or null when cancelled.
/// </summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog()
    {
        InitializeComponent();
    }

    public TextInputDialog(string title, string message, string initialValue)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        InputBox.Text = initialValue;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close(InputBox.Text);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
