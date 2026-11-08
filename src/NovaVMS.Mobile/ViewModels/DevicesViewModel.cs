using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Saved devices with channel download and editing.</summary>
public sealed partial class DevicesViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;

    public override MobileTab Tab => MobileTab.Devices;

    public bool StorageReady => _shell.StorageReady;

    public bool StorageUnavailable => !_shell.StorageReady;

    public IReadOnlyList<DeviceItemViewModel> Devices { get; }

    public DevicesViewModel(ShellViewModel shell)
    {
        _shell = shell;

        Devices = shell.StorageReady
            ? shell.Devices.Select(device => new DeviceItemViewModel(shell, device)).ToList()
            : [];
    }

    [RelayCommand]
    private void Add()
    {
        _shell.EditDevice(null);
    }
}
