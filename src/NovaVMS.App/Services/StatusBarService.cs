using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;

namespace NovaVMS.App.Services;

/// <summary>
/// Shared status bar message.
/// </summary>
public sealed partial class StatusBarService : ObservableObject
{
    [ObservableProperty]
    private string _message = Localizer.Text("Status_Ready");

    public StatusBarService()
    {
        DeviceOperationErrors.ErrorReported += message => Message = message;
    }
}
