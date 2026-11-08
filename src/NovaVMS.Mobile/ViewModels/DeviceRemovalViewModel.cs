using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Confirmation before a device and its saved channels are deleted.</summary>
public sealed partial class DeviceRemovalViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly Device _device;

    public override MobileTab Tab => MobileTab.Settings;

    public string Message => Localizer.Format("Mobile_DeleteConfirm", _device.DisplayName);

    public DeviceRemovalViewModel(ShellViewModel shell, Device device)
    {
        _shell = shell;
        _device = device;
    }

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowDevices();
    }

    [RelayCommand]
    private Task DeleteAsync()
    {
        return _shell.RunAsync(async token =>
        {
            await _shell.ReplaceDeviceAsync(_device, null, token);

            _shell.ShowDevices();
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        _shell.EditDevice(_device);
    }
}
