using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.App.Localization;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// PTZ control of the camera shown in the selected live view tile.
/// </summary>
public sealed partial class PtzViewModel : ViewModelBase, IDisposable
{
    private const string NoTargetKey = "Ptz_NoTarget";
    private const int SpeedScale = 14;

    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clientFactory;
    private readonly StatusBarService _statusBar;
    // Serializes commands so that "stop" never overtakes "move".
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private CameraReference? _camera;
    // Camera that received the last move, so "stop" reaches it even if the selected tile changed meanwhile.
    private CameraReference? _movingCamera;
    // One client per device: reuses its HTTP connection / SDK session and remembers the active SDK PTZ
    // command, which the stop command needs. Replaced when commands go to another device.
    private IDeviceClient? _client;
    private Device? _clientDevice;
    private string? _clientSettings;
    private bool _isDisposed;
    private Task? _disposal;
    public Task Disposal => _disposal ?? Task.CompletedTask;

    [ObservableProperty]
    private string _targetName = Localizer.Text(NoTargetKey);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPresetsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GotoPresetCommand))]
    private bool _hasTarget;

    [ObservableProperty]
    private double _speed = 4;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GotoPresetCommand))]
    private PtzPreset? _selectedPreset;

    public PtzViewModel(ConfigurationService configuration, IDeviceClientFactory clientFactory, StatusBarService statusBar)
    {
        _configuration = configuration;
        _clientFactory = clientFactory;
        _statusBar = statusBar;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<PtzPreset> Presets { get; } = new();

    public void SetTarget(CameraReference? camera)
    {
        if (Equals(_camera, camera))
        {
            return;
        }

        _camera = camera;
        Presets.Clear();
        SelectedPreset = null;

        var descriptor = camera is null ? null : _configuration.FindCamera(camera);
        TargetName = descriptor?.FullName ?? Localizer.Text(NoTargetKey);
        HasTarget = descriptor is not null;
    }

    public async Task StartMoveAsync(PtzDirection direction)
    {
        var (pan, tilt, zoom) = ToVector(direction);
        var scale = (int)Math.Round(Speed) * SpeedScale;

        _movingCamera = _camera;
        await ExecuteAsync(_movingCamera, (client, channel) => client.PtzMoveAsync(channel, pan * scale, tilt * scale, zoom * scale));
    }

    public async Task StopMoveAsync()
    {
        var camera = _movingCamera;
        if (camera is null)
        {
            return;
        }

        _movingCamera = null;
        await ExecuteAsync(camera, (client, channel) => client.PtzStopAsync(channel));
    }

    public void Dispose()
        => _disposal ??= DisposeCoreAsync();

    private async Task DisposeCoreAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        // A command can still be inside an SDK call. Keep its client and the SDK alive until it returns.
        await _commandGate.WaitAsync();
        try { ReleaseClient(); }
        finally { _commandGate.Release(); }
    }

    [RelayCommand(CanExecute = nameof(HasTarget))]
    private async Task LoadPresetsAsync()
    {
        await ExecuteAsync(_camera, async (client, channel) =>
        {
            var presets = await client.GetPtzPresetsAsync(channel);

            Presets.Clear();
            foreach (var preset in presets)
            {
                Presets.Add(preset);
            }

            SelectedPreset = Presets.FirstOrDefault();
            _statusBar.Message = Localizer.Format("Ptz_PresetsLoaded", presets.Count);
        });
    }

    [RelayCommand(CanExecute = nameof(CanGotoPreset))]
    private async Task GotoPresetAsync()
    {
        var preset = SelectedPreset;
        if (preset is null)
        {
            return;
        }

        await ExecuteAsync(_camera, (client, channel) => client.GotoPtzPresetAsync(channel, preset));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!HasTarget)
        {
            TargetName = Localizer.Text(NoTargetKey);
        }
    }

    private bool CanGotoPreset()
    {
        return HasTarget && SelectedPreset is not null;
    }

    private async Task ExecuteAsync(CameraReference? target, Func<IDeviceClient, int, Task> action)
    {
        var camera = target is null ? null : _configuration.FindCamera(target);
        if (camera is null)
        {
            return;
        }

        await _commandGate.WaitAsync();
        try
        {
            if (_isDisposed)
            {
                return;
            }

            var client = await GetClientAsync(camera.Device);
            if (_isDisposed) return;
            if (client is IDeviceConnectionClient connectionClient)
            {
                var connection = await connectionClient.TryConnectAsync();
                if (_isDisposed) return;
                if (!connection.Success)
                {
                    var message = DeviceOperationErrors.Report($"PTZ control: {camera.FullName}", connection.Error!);
                    _statusBar.Message = Localizer.Format("Ptz_Error", message);
                    return;
                }
            }
            await action(client, camera.Channel.Number);
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            // Start the next command with a fresh client and connection.
            ReleaseClient();
            DeviceOperationErrors.Report($"PTZ control: {camera.FullName}", exception);
            _statusBar.Message = Localizer.Format("Ptz_Error", exception.Message);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<IDeviceClient> GetClientAsync(Device device)
    {
        // Applying settings replaces the Device object and editing a device changes it in place;
        // either way a client with outdated connection settings must not be reused.
        var settings = device.GetConnectionSignature();
        if (_client is not null && ReferenceEquals(_clientDevice, device) && _clientSettings == settings)
        {
            return _client;
        }

        ReleaseClient();
        _client = await _clientFactory.CreateAsync(device);
        _clientDevice = device;
        _clientSettings = settings;
        return _client;
    }


    private void ReleaseClient()
    {
        _client?.Dispose();
        _client = null;
        _clientDevice = null;
        _clientSettings = null;
    }

    private static (int Pan, int Tilt, int Zoom) ToVector(PtzDirection direction)
    {
        return direction switch
        {
            PtzDirection.Up => (0, 1, 0),
            PtzDirection.Down => (0, -1, 0),
            PtzDirection.Left => (-1, 0, 0),
            PtzDirection.Right => (1, 0, 0),
            PtzDirection.UpLeft => (-1, 1, 0),
            PtzDirection.UpRight => (1, 1, 0),
            PtzDirection.DownLeft => (-1, -1, 0),
            PtzDirection.DownRight => (1, -1, 0),
            PtzDirection.ZoomIn => (0, 0, 1),
            PtzDirection.ZoomOut => (0, 0, -1),
            _ => (0, 0, 0)
        };
    }
}
