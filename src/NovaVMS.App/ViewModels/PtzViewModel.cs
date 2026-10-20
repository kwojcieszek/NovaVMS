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
public sealed partial class PtzViewModel : ViewModelBase
{
    private const string NoTargetKey = "Ptz_NoTarget";
    private const int SpeedScale = 14;

    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clientFactory;
    private readonly StatusBarService _statusBar;
    // Serializes commands so that "stop" never overtakes "move".
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private CameraReference? _camera;
    private bool _isMoving;

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

        _isMoving = true;
        await ExecuteAsync((client, channel) => client.PtzMoveAsync(channel, pan * scale, tilt * scale, zoom * scale));
    }

    public async Task StopMoveAsync()
    {
        if (!_isMoving)
        {
            return;
        }

        _isMoving = false;
        await ExecuteAsync((client, channel) => client.PtzStopAsync(channel));
    }

    [RelayCommand(CanExecute = nameof(HasTarget))]
    private async Task LoadPresetsAsync()
    {
        await ExecuteAsync(async (client, channel) =>
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

        await ExecuteAsync((client, channel) => client.GotoPtzPresetAsync(channel, preset));
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

    private async Task ExecuteAsync(Func<IDeviceClient, int, Task> action)
    {
        var camera = _camera is null ? null : _configuration.FindCamera(_camera);
        if (camera is null)
        {
            return;
        }

        await _commandGate.WaitAsync();
        try
        {
            using var client = _clientFactory.Create(camera.Device);
            if (client is IDeviceConnectionClient connectionClient)
            {
                var connection = await connectionClient.TryConnectAsync();
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
            DeviceOperationErrors.Report($"PTZ control: {camera.FullName}", exception);
            _statusBar.Message = Localizer.Format("Ptz_Error", exception.Message);
        }
        finally
        {
            _commandGate.Release();
        }
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
