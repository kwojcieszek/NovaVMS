using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Device form with the same fields as the desktop device settings.</summary>
public sealed partial class DeviceEditViewModel : PageViewModel
{
    private const int MaxStreamKeyBytes = 128;

    private readonly ShellViewModel _shell;
    private readonly Device? _existing;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _host;

    [ObservableProperty]
    private string _secondaryHost;

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _password;

    [ObservableProperty]
    private string _httpPort;

    [ObservableProperty]
    private string _rtspPort;

    [ObservableProperty]
    private string _sdkPort;

    [ObservableProperty]
    private string _secondaryHttpPort;

    [ObservableProperty]
    private string _secondarySdkPort;

    [ObservableProperty]
    private string _secondaryRtspPort;

    [ObservableProperty]
    private bool _useHttps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSdkProtocol))]
    [NotifyPropertyChangedFor(nameof(IsNetworkProtocol))]
    private ProtocolOption _protocol;

    [ObservableProperty]
    private bool _hasOptionalProtocol;

    [ObservableProperty]
    private ProtocolOption _optionalProtocol;

    [ObservableProperty]
    private bool _optionalUseHttps;

    [ObservableProperty]
    private string _streamEncryptionKey;

    public override MobileTab Tab => MobileTab.Devices;

    public string Title => Localizer.Text(_existing is null ? "Mobile_NewDevice" : "Mobile_EditDevice");

    public bool CanDelete => _existing is not null;

    public bool IsSdkProtocol => Protocol.Value == DeviceProtocol.HikvisionSdk;

    public bool IsNetworkProtocol => !IsSdkProtocol;

    public IReadOnlyList<ProtocolOption> Protocols => ProtocolOption.All;

    public DeviceEditViewModel(ShellViewModel shell, Device? existing)
    {
        var device = existing ?? new Device();

        _shell = shell;
        _existing = existing;
        _name = device.Name;
        _host = device.Host;
        _secondaryHost = device.SecondaryHost;
        _username = device.Username;
        _password = device.Password;
        _httpPort = Format(device.HttpPort);
        _rtspPort = Format(device.RtspPort);
        _sdkPort = Format(device.SdkPort);
        _secondaryHttpPort = Format(device.SecondaryHttpPort ?? (device.Protocol != DeviceProtocol.HikvisionSdk ? device.SecondaryPort : null));
        _secondarySdkPort = Format(device.SecondarySdkPort ?? (device.Protocol == DeviceProtocol.HikvisionSdk ? device.SecondaryPort : null));
        _secondaryRtspPort = Format(device.SecondaryRtspPort);
        _useHttps = device.UseHttps;
        _protocol = ProtocolOption.Find(device.Protocol);
        _hasOptionalProtocol = device.OptionalConnection is not null;
        _optionalProtocol = ProtocolOption.Find(device.OptionalConnection?.Protocol ?? DeviceProtocol.Isapi);
        _optionalUseHttps = device.OptionalConnection?.UseHttps ?? false;
        _streamEncryptionKey = device.StreamEncryptionKey;
    }

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowDevices();
    }

    private static string Format(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool TryParsePort(string text, out int port)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
    }

    private static bool TryParseOptionalPort(string text, out int? port)
    {
        port = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!TryParsePort(text, out var value) || value is < 1 or > 65535)
        {
            return false;
        }

        port = value;

        return true;
    }

    [RelayCommand]
    private Task SaveAsync()
    {
        return _shell.RunAsync(SaveDeviceAsync);
    }

    [RelayCommand]
    private void Cancel()
    {
        _shell.CancelPending();
        _shell.ShowDevices();
    }

    [RelayCommand]
    private void Delete()
    {
        if (_existing is not null)
        {
            _shell.ConfirmDeviceRemoval(_existing);
        }
    }

    private async Task SaveDeviceAsync(CancellationToken token)
    {
        if (!TryParsePort(HttpPort, out var httpPort) || !TryParsePort(RtspPort, out var rtspPort) || !TryParsePort(SdkPort, out var sdkPort))
        {
            _shell.Status = Localizer.Text("Mobile_PortsMustBeNumbers");

            return;
        }

        if (!TryParseOptionalPort(SecondaryHttpPort, out var secondaryHttpPort)
            || !TryParseOptionalPort(SecondarySdkPort, out var secondarySdkPort)
            || !TryParseOptionalPort(SecondaryRtspPort, out var secondaryRtspPort))
        {
            _shell.Status = Localizer.Text("Devices_InvalidSecondaryPort");

            return;
        }

        if (StreamEncryptionKey.Contains('\0') || Encoding.UTF8.GetByteCount(StreamEncryptionKey) > MaxStreamKeyBytes)
        {
            _shell.Status = Localizer.Text("Devices_StreamKeyInvalid");

            return;
        }

        var device = _existing?.ForHost(_existing.Host) ?? new Device();

        device.Name = Name.Trim();
        device.Host = Host.Trim();
        device.SecondaryHost = SecondaryHost.Trim();
        device.SecondarySdkPort = secondarySdkPort;
        device.SecondaryHttpPort = secondaryHttpPort;
        device.SecondaryPort = IsSdkProtocol ? secondarySdkPort : secondaryHttpPort;
        device.SecondaryRtspPort = secondaryRtspPort;
        device.HttpPort = httpPort;
        device.RtspPort = rtspPort;
        device.SdkPort = sdkPort;
        device.UseHttps = UseHttps;
        device.Username = Username.Trim();
        device.Password = Password;
        device.StreamEncryptionKey = StreamEncryptionKey;
        device.Channels = _existing?.Channels ?? new List<Channel>();

        if (_existing is not null && _existing.Protocol != Protocol.Value)
        {
            // Stream addresses and profiles belong to the previous protocol. The channels themselves are kept,
            // so the cameras keep their identity in views and groups when they are read again.
            device.Channels = device.Channels.Select(channel => new Channel
            {
                Id = channel.Id,
                Number = channel.Number,
                Name = channel.Name,
                Enabled = channel.Enabled,
                IsFavorite = channel.IsFavorite
            }).ToList();
        }

        device.Protocol = Protocol.Value;
        device.OptionalConnection = HasOptionalProtocol ? new DeviceConnectionSettings
        {
            Protocol = OptionalProtocol.Value,
            Host = device.Host,
            SecondaryHost = device.SecondaryHost,
            SecondaryPort = OptionalProtocol.Value == DeviceProtocol.HikvisionSdk ? secondarySdkPort : secondaryHttpPort,
            SecondaryRtspPort = secondaryRtspPort,
            HttpPort = httpPort,
            RtspPort = rtspPort,
            SdkPort = sdkPort,
            UseHttps = OptionalUseHttps
        } : null;

        if (MobileDevices.ValidateAddress(device) is { } error)
        {
            _shell.Status = error;

            return;
        }

        if (device.SecondaryHost.Length > 0 && Uri.CheckHostName(device.SecondaryHost) == UriHostNameType.Unknown)
        {
            _shell.Status = Localizer.Text("Mobile_InvalidSecondaryHost");

            return;
        }

        await _shell.ReplaceDeviceAsync(_existing, device, token);

        _shell.ShowDevices();
        _shell.Status = Localizer.Text("Mobile_DeviceSaved");
    }
}
