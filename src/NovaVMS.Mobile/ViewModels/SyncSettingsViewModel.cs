using NovaVMS.Mobile.Models;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Settings page with the synchronization form shared with the desktop application; questions are asked over the page.
/// </summary>
public sealed class SyncSettingsViewModel : PageViewModel
{
    public override MobileTab Tab => MobileTab.Settings;

    public SyncSettingsForm Form { get; }

    public SyncSettingsViewModel(ShellViewModel shell)
    {
        Form = new SyncSettingsForm(shell.Sync, shell.ConfirmAsync);
    }

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowSettings();
    }

    /// <summary>Stops following the synchronization service once the page is replaced.</summary>
    public void Detach()
    {
        Form.Dispose();
    }
}
