using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using NovaVMS.Mobile.ViewModels;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Mobile.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LogEntryItemViewModel entry }
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard
            || DataContext is not LogViewModel log)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(entry.FullText);

            log.Status = Localizer.Text("Log_Copied");
        }
        catch
        {
            // The clipboard can be unavailable while the application is in the background.
        }
    }
}
