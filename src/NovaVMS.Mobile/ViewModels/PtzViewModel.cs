using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Services;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>PTZ control: each tap sends a bounded movement pulse followed by stop.</summary>
public sealed partial class PtzViewModel : ViewModelBase
{
    private const int Speed = 40;
    private const int StopTimeoutMilliseconds = 3000;

    private readonly ShellViewModel _shell;
    private readonly IDeviceClient _client;
    private readonly Channel _channel;

    public ObservableCollection<PresetItemViewModel> Presets { get; } = new();

    public PtzViewModel(ShellViewModel shell, IDeviceClient client, Channel channel)
    {
        _shell = shell;
        _client = client;
        _channel = channel;
    }

    [RelayCommand]
    private Task UpAsync()
    {
        return PulseAsync(0, Speed, 0);
    }

    [RelayCommand]
    private Task DownAsync()
    {
        return PulseAsync(0, -Speed, 0);
    }

    [RelayCommand]
    private Task LeftAsync()
    {
        return PulseAsync(-Speed, 0, 0);
    }

    [RelayCommand]
    private Task RightAsync()
    {
        return PulseAsync(Speed, 0, 0);
    }

    [RelayCommand]
    private Task ZoomInAsync()
    {
        return PulseAsync(0, 0, Speed);
    }

    [RelayCommand]
    private Task ZoomOutAsync()
    {
        return PulseAsync(0, 0, -Speed);
    }

    [RelayCommand]
    private Task StopAsync()
    {
        return _shell.GuardAsync(() => _shell.RunExclusiveAsync(async () =>
        {
            if (_shell.IsCurrentClient(_client))
            {
                using var timeout = new CancellationTokenSource(StopTimeoutMilliseconds);

                await _client.PtzStopAsync(_channel.Number, timeout.Token);
            }
        }));
    }

    [RelayCommand]
    private Task LoadPresetsAsync()
    {
        return _shell.RunAsync(async token =>
        {
            if (!_shell.IsCurrentClient(_client))
            {
                return;
            }

            var presets = await _client.GetPtzPresetsAsync(_channel.Number, token);

            Presets.Clear();

            foreach (var preset in presets)
            {
                Presets.Add(new PresetItemViewModel(_shell, _client, _channel, preset));
            }
        });
    }

    private Task PulseAsync(int pan, int tilt, int zoom)
    {
        return _shell.RunAsync(async token =>
        {
            if (_shell.IsCurrentClient(_client))
            {
                await MobileDevices.PulseAsync(_client, _channel.Number, pan, tilt, zoom, token);
            }
        });
    }
}
