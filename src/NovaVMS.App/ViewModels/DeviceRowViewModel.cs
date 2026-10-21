using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;
using NovaVMS.App.Models;
using NovaVMS.Core.Models;
using NovaVMS.Core.Abstractions;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Row of the device table in the settings window.
/// </summary>
public sealed partial class DeviceRowViewModel : ViewModelBase
{
    private readonly IDeviceClientFactory? _clientFactory;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline))]
    [NotifyPropertyChangedFor(nameof(IsOffline))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private DeviceConnectionState _state = DeviceConnectionState.Unknown;

    public DeviceRowViewModel(Device device, IDeviceClientFactory? clientFactory = null)
    {
        Device = device;
        _clientFactory = clientFactory;
    }

    public Device Device { get; }

    public string Name => Device.DisplayName;

    public string Address => Device.Host;

    public string SecondaryAddress => Device.SecondaryHost;

    public int? SecondaryPort => string.IsNullOrWhiteSpace(Device.SecondaryHost) ? null : Device.SecondaryPort ?? Port;

    public int? SecondaryRtspPort => string.IsNullOrWhiteSpace(Device.SecondaryHost) ? null : Device.SecondaryRtspPort ?? Device.RtspPort;

    public string ActiveAddress => _clientFactory?.GetSelectedHost(Device) is { } host
        ? $"{host}:{_clientFactory.GetSelectedConnection(Device).ConnectionPort}"
        : Localizer.Text("Devices_AddressNotSelected");

    public int Port => Device.Protocol == DeviceProtocol.HikvisionSdk ? Device.SdkPort : Device.HttpPort;

    public string Protocol => ProtocolOption.Find(Device.Protocol).Label;

    public string Type => Localizer.Text($"DeviceType_{Device.Type}");

    public string Model => Device.Model;

    public string SerialNumber => Device.SerialNumber;

    public string FirmwareVersion => Device.FirmwareVersion;

    public int ChannelCount => Device.Channels.Count;

    public bool IsOnline => State == DeviceConnectionState.Online;

    public bool IsOffline => State == DeviceConnectionState.Offline;

    public bool IsChecking => State is DeviceConnectionState.Checking or DeviceConnectionState.Unknown;

    public string StateText => Localizer.Text($"DeviceState_{State}");

    /// <summary>
    /// Raises change notifications after the device was edited or synchronized, or the language changed.
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
    }
}
