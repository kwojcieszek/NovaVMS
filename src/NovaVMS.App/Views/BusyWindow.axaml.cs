using Avalonia.Controls;
using NovaVMS.App.Localization;

namespace NovaVMS.App.Views;

/// <summary>
/// Small window with a progress bar shown while the application starts or closes. The user cannot close it;
/// the operation closes it with <see cref="Finish"/> when it is done.
/// </summary>
public partial class BusyWindow : Window
{
    private bool _canClose;

    public BusyWindow()
    {
        InitializeComponent();
    }

    public BusyWindow(string titleKey, string messageKey)
        : this()
    {
        TitleText.Text = Localizer.Text(titleKey);
        MessageText.Text = Localizer.Text(messageKey);
    }

    public void Finish()
    {
        _canClose = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = !_canClose;
        base.OnClosing(e);
    }
}
