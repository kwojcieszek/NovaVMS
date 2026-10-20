using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Import;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Settings page: device table with a periodic reachability check, and adding, editing,
/// removing and discovering devices.
/// </summary>
public sealed partial class DevicesSettingsViewModel : ViewModelBase, IDisposable
{
    private const string NewDeviceTitleKey = "Devices_New";

    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StateCheckInterval = TimeSpan.FromSeconds(30);

    private readonly ConfigurationService _configuration;
    private readonly DeviceSyncService _sync;
    private readonly IDeviceDiscovery _discovery;
    private readonly IDeviceReachability _reachability;
    private readonly IDialogService _dialogs;
    private readonly DeviceCsvReader _csvReader;
    private readonly DispatcherTimer _stateTimer;
    private bool _isCheckingStates;
    private bool _isDisposed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshChannelsCommand))]
    private DeviceRowViewModel? _selectedDevice;

    [ObservableProperty]
    private DiscoveredDevice? _selectedDiscoveredDevice;

    [ObservableProperty]
    private string _formTitle = Localizer.Text(NewDeviceTitleKey);

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDeviceCommand))]
    private string _editHost = string.Empty;

    [ObservableProperty]
    private int _editHttpPort = 80;

    [ObservableProperty]
    private int _editRtspPort = 554;

    [ObservableProperty]
    private int _editSdkPort = 8000;

    [ObservableProperty]
    private bool _editUseHttps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSdkProtocol))]
    [NotifyPropertyChangedFor(nameof(IsNetworkProtocol))]
    private ProtocolOption _editProtocol = ProtocolOption.Find(DeviceProtocol.Isapi);

    [ObservableProperty]
    private string _editUsername = "admin";

    [ObservableProperty]
    private string _editPassword = string.Empty;

    [ObservableProperty]
    private string _editStreamEncryptionKey = string.Empty;

    [ObservableProperty]
    private string _infoText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public DevicesSettingsViewModel(
        ConfigurationService configuration,
        DeviceSyncService sync,
        IDeviceDiscovery discovery,
        IDeviceReachability reachability,
        IDialogService dialogs,
        DeviceCsvReader csvReader)
    {
        _configuration = configuration;
        _sync = sync;
        _discovery = discovery;
        _reachability = reachability;
        _dialogs = dialogs;
        _csvReader = csvReader;

        foreach (var device in _configuration.Current.Devices)
        {
            Devices.Add(new DeviceRowViewModel(device));
        }

        _stateTimer = new DispatcherTimer
        {
            Interval = StateCheckInterval
        };
        _stateTimer.Tick += OnStateTimerTick;
        _stateTimer.Start();
        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        _ = CheckStatesAsync();
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();

    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();

    public IReadOnlyList<ProtocolOption> Protocols => ProtocolOption.All;

    /// <summary>
    /// The Hikvision SDK protocol uses its own port instead of the HTTP and RTSP ports.
    /// </summary>
    public bool IsSdkProtocol => EditProtocol.Value == DeviceProtocol.HikvisionSdk;

    public bool IsNetworkProtocol => !IsSdkProtocol;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _stateTimer.Stop();
        _stateTimer.Tick -= OnStateTimerTick;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
    }

    [RelayCommand]
    private void NewDevice()
    {
        SelectedDevice = null;
        ClearForm();
    }

    [RelayCommand]
    private Task CheckStatesNowAsync()
    {
        return CheckStatesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSaveDevice))]
    private async Task SaveDeviceAsync()
    {
        if (EditStreamEncryptionKey.Contains('\0') || Encoding.UTF8.GetByteCount(EditStreamEncryptionKey) > 128)
        {
            InfoText = Localizer.Text("Devices_StreamKeyInvalid");
            return;
        }
        var row = SelectedDevice;
        var device = row?.Device ?? new Device();

        device.Name = EditName.Trim();
        device.Host = EditHost.Trim();
        device.HttpPort = EditHttpPort;
        device.RtspPort = EditRtspPort;
        device.SdkPort = EditSdkPort;
        device.UseHttps = EditUseHttps;
        if (device.Protocol != EditProtocol.Value)
        {
            // Stream addresses and profiles belong to the previous protocol; channels are read again.
            device.Protocol = EditProtocol.Value;
            device.Channels = new List<Channel>();
        }
        device.Username = EditUsername.Trim();
        device.Password = EditPassword;
        device.StreamEncryptionKey = EditStreamEncryptionKey;

        if (row is null)
        {
            _configuration.Current.Devices.Add(device);
            row = new DeviceRowViewModel(device);
            Devices.Add(row);
        }

        await SyncAndSaveAsync(device);
        RefreshRow(row);
        await CheckStateAsync(row);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedDevice))]
    private async Task RefreshChannelsAsync()
    {
        var row = SelectedDevice;
        if (row is null)
        {
            return;
        }

        await SyncAndSaveAsync(row.Device);
        RefreshRow(row);
        await CheckStateAsync(row);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedDevice))]
    private async Task DeleteDeviceAsync()
    {
        var row = SelectedDevice;
        if (row is null)
        {
            return;
        }

        var device = row.Device;
        var confirmed = await _dialogs.ConfirmAsync(Localizer.Text("Devices_DeleteTitle"), Localizer.Format("Devices_DeleteConfirm", device.DisplayName));
        if (!confirmed)
        {
            return;
        }

        _configuration.RemoveDevice(device.Id);
        await _configuration.SaveAsync();

        Devices.Remove(row);
        SelectedDevice = null;
        InfoText = Localizer.Format("Devices_Deleted", device.DisplayName);
    }

    [RelayCommand]
    private async Task DiscoverAsync()
    {
        DiscoveredDevices.Clear();
        InfoText = Localizer.Text("Devices_Searching");

        try
        {
            var found = await _discovery.DiscoverAsync(DiscoveryTimeout);
            foreach (var device in found)
            {
                DiscoveredDevices.Add(device);
            }

            InfoText = found.Count == 0
                ? Localizer.Text("Devices_NoneFound")
                : Localizer.Format("Devices_Found", found.Count);
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            DeviceOperationErrors.Report("Device discovery", exception);
            InfoText = Localizer.Format("Devices_DiscoveryFailed", exception.Message);
        }
    }

    [RelayCommand]
    private async Task ImportCsvAsync()
    {
        DeviceCsvResult result;
        await using (var stream = await _dialogs.OpenCsvFileAsync(Localizer.Text("Devices_ImportCsvTitle")))
        {
            if (stream is null)
            {
                return;
            }

            try
            {
                result = await _csvReader.ReadAsync(stream);
            }
            catch (FormatException exception)
            {
                InfoText = Localizer.Format("Devices_ImportFailed", exception.Message);
                return;
            }
        }

        await ImportDevicesAsync(result);
    }

    [RelayCommand]
    private async Task ImportIvmsAsync()
    {
        try
        {
            var source = await _dialogs.OpenIvmsFileAsync(Localizer.Text("Devices_IvmsTitle"));
            if (source is null) return;
            DeviceCsvResult result;
            IsBusy = true;
            InfoText = Localizer.Text("Devices_IvmsReading");
            await using (var content = source.Value.Content)
            await using (var setup = source.Value.Setup)
                result = await new IvmsDeviceReader().ReadAsync(content, setup);
            IsBusy = false;
            await ImportDevicesAsync(result);
        }
        catch (Exception exception) when (exception is FormatException or IOException or InvalidDataException
            or System.Data.Common.DbException or UnauthorizedAccessException)
        {
            InfoText = Localizer.Format("Devices_ImportFailed", exception.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task ImportDevicesAsync(DeviceCsvResult result)
    {
        var existing = _configuration.Current.Devices
            .Select(device => DeviceKey(device))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newDevices = result.Devices
            .Where(device => existing.Add(DeviceKey(device)))
            .ToList();
        var skipped = result.Devices.Count - newDevices.Count;

        if (newDevices.Count == 0)
        {
            InfoText = result.Devices.Count == 0
                ? Localizer.Text("Devices_ImportEmpty")
                : Localizer.Text("Devices_ImportAllExist");
            return;
        }

        var message = Localizer.Format("Devices_ImportConfirm", newDevices.Count);
        if (skipped > 0)
        {
            message += "\n" + Localizer.Format("Devices_ImportSkippedNote", skipped);
        }

        if (!await _dialogs.ConfirmAsync(Localizer.Text("Devices_ImportTitle"), message))
        {
            return;
        }

        var rows = new List<DeviceRowViewModel>();
        foreach (var device in newDevices)
        {
            _configuration.Current.Devices.Add(device);
            var row = new DeviceRowViewModel(device);
            Devices.Add(row);
            rows.Add(row);
            // Imported channel names remain available even if the recorder is offline.
            _configuration.SyncDeviceGroup(device, allowCreate: device.Channels.Count > 0);
        }

        await _configuration.SaveAsync();

        IsBusy = true;
        InfoText = Localizer.Format("Devices_ImportConnecting", newDevices.Count);
        bool[] connected;
        try
        {
            connected = await Task.WhenAll(newDevices.Select(TryConnectImportedDeviceAsync));
            await _configuration.SaveAsync();
        }
        finally { IsBusy = false; }

        foreach (var row in rows)
        {
            row.Refresh();
        }

        _ = CheckStatesAsync();

        var summary = Localizer.Format("Devices_ImportSummary", newDevices.Count, connected.Count(success => success));
        if (skipped > 0)
        {
            summary += Localizer.Format("Devices_ImportSkipped", skipped);
        }

        var notes = result.Warnings.Take(8).ToList();
        if (result.Warnings.Count > notes.Count)
        {
            notes.Add(Localizer.Format("Devices_ImportMoreNotes", result.Warnings.Count - notes.Count));
        }

        InfoText = notes.Count == 0 ? summary : summary + "\n\n" + string.Join("\n", notes);
    }

    [RelayCommand]
    private async Task SaveCsvTemplateAsync()
    {
        await using var stream = await _dialogs.SaveCsvFileAsync(Localizer.Text("Devices_SaveTemplateTitle"), "novavms-devices.csv");
        if (stream is null)
        {
            return;
        }

        // UTF-8 with a byte order mark, so spreadsheet programs read non-ASCII names correctly.
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            await writer.WriteAsync(DeviceCsvReader.Template);
        }

        if (stream.CanSeek)
        {
            stream.SetLength(stream.Position);
        }

        InfoText = Localizer.Text("Devices_TemplateSaved");
    }

    private static string DeviceKey(Device device)
    {
        var port = device.Protocol == DeviceProtocol.HikvisionSdk ? device.SdkPort : device.HttpPort;
        return $"{device.Protocol}:{device.Host.Trim()}:{port}";
    }

    private async Task<bool> TryConnectImportedDeviceAsync(Device device)
    {
        var importedNames = device.Channels.ToDictionary(channel => channel.Number, channel => channel.Name);
        var result = await _sync.TrySyncAsync(device);
        if (!result.Success) return false;
        foreach (var channel in device.Channels)
        {
            if (importedNames.TryGetValue(channel.Number, out var name) && !string.IsNullOrWhiteSpace(name))
                channel.Name = name;
        }
        _configuration.SyncDeviceGroup(device, allowCreate: true);
        return true;
    }

    private bool CanSaveDevice()
    {
        return !string.IsNullOrWhiteSpace(EditHost);
    }

    private bool HasSelectedDevice()
    {
        return SelectedDevice is not null;
    }

    partial void OnSelectedDeviceChanged(DeviceRowViewModel? value)
    {
        if (value is null)
        {
            ClearForm();
            return;
        }

        var device = value.Device;
        FormTitle = device.DisplayName;
        EditName = device.Name;
        EditHost = device.Host;
        EditHttpPort = device.HttpPort;
        EditRtspPort = device.RtspPort;
        EditSdkPort = device.SdkPort;
        EditUseHttps = device.UseHttps;
        EditProtocol = ProtocolOption.Find(device.Protocol);
        EditUsername = device.Username;
        EditPassword = device.Password;
        EditStreamEncryptionKey = device.StreamEncryptionKey;
        InfoText = string.Empty;
    }

    partial void OnSelectedDiscoveredDeviceChanged(DiscoveredDevice? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedDevice = null;
        EditHost = value.Address;
        EditName = value.Name;
        if (Uri.TryCreate(value.ServiceAddress, UriKind.Absolute, out var serviceUri))
        {
            EditHttpPort = serviceUri.Port;
        }

        EditProtocol = ProtocolOption.ForDiscovered(value);
    }

    private async Task SyncAndSaveAsync(Device device)
    {
        IsBusy = true;
        InfoText = Localizer.Text("Devices_Connecting");

        try
        {
            var result = await _sync.TrySyncAsync(device);
            if (result.Success && result.Info is { } info)
            {
                _configuration.SyncDeviceGroup(device, allowCreate: true);
                InfoText = Localizer.Format("Devices_Connected", info.Model, info.SerialNumber, info.FirmwareVersion, device.Channels.Count);
            }
            else InfoText = Localizer.Format("Devices_SavedNotConnected", result.ErrorMessage);
        }
        finally
        {
            try { await _configuration.SaveAsync(); }
            catch (Exception exception) when (DeviceFailure.IsExpected(exception))
            {
                InfoText = DeviceOperationErrors.Report("Saving device configuration", exception);
            }
            finally { IsBusy = false; }
        }
    }

    private void RefreshRow(DeviceRowViewModel row)
    {
        row.Refresh();
        var message = InfoText;
        SelectedDevice = row;
        FormTitle = row.Device.DisplayName;
        InfoText = message;
    }

    /// <summary>
    /// Checks all devices in parallel; a check still running when the timer fires again is not repeated.
    /// </summary>
    private async Task CheckStatesAsync()
    {
        if (_isCheckingStates || _isDisposed)
        {
            return;
        }

        _isCheckingStates = true;
        try
        {
            await Task.WhenAll(Devices.ToList().Select(CheckStateAsync));
        }
        finally
        {
            _isCheckingStates = false;
        }
    }

    private async Task CheckStateAsync(DeviceRowViewModel row)
    {
        if (row.State != DeviceConnectionState.Online && row.State != DeviceConnectionState.Offline)
        {
            row.State = DeviceConnectionState.Checking;
        }

        bool reachable;
        try { reachable = await _reachability.IsReachableAsync(row.Device); }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            reachable = false;
            DeviceOperationErrors.Report($"Device availability: {row.Device.DisplayName}", exception);
        }
        if (!_isDisposed)
        {
            row.State = reachable ? DeviceConnectionState.Online : DeviceConnectionState.Offline;
        }
    }

    private void OnStateTimerTick(object? sender, EventArgs e)
    {
        _ = CheckStatesAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var row in Devices)
        {
            row.Refresh();
        }
    }

    private void ClearForm()
    {
        FormTitle = Localizer.Text(NewDeviceTitleKey);
        EditName = string.Empty;
        EditHost = string.Empty;
        EditHttpPort = 80;
        EditRtspPort = 554;
        EditSdkPort = 8000;
        EditUseHttps = false;
        EditProtocol = ProtocolOption.Find(DeviceProtocol.Isapi);
        EditUsername = "admin";
        EditPassword = string.Empty;
        EditStreamEncryptionKey = string.Empty;
        InfoText = string.Empty;
    }
}
