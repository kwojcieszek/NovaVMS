using Avalonia.Controls;
using NovaVMS.App.ViewModels;
using NovaVMS.App.Services;

namespace NovaVMS.App.Views;

public partial class SettingsWindow : Window
{
    private bool _closing;
    private bool _canClose;
    public SettingsWindow() => InitializeComponent();
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_canClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (_closing) return;
        _closing = true;
        try
        {
            if (DataContext is SettingsWindowViewModel vm)
            {
                vm.Groups.Preview.Dispose();
                await vm.Groups.Preview.Disposal;
            }
        }
        catch (Exception exception) { UnhandledExceptionLogger.Log("Closing group preview", exception); }
        finally { _canClose = true; Close(); }
    }
}
