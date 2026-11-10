using NovaVMS.App.Services;
using NovaVMS.Presentation.Services;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Settings page with the synchronization form shared with the mobile application; questions open desktop dialogs.
/// </summary>
public sealed class SyncSettingsViewModel : ViewModelBase, IDisposable
{
    public SyncSettingsForm Form { get; }

    public SyncSettingsViewModel(SettingsSyncService sync, IDialogService dialogs)
    {
        Form = new SyncSettingsForm(sync, dialogs.ConfirmAsync);
    }

    public void Dispose()
    {
        Form.Dispose();
    }
}
