using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>PTZ preset button.</summary>
public sealed partial class PresetItemViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly IDeviceClient _client;
    private readonly Channel _channel;
    private readonly PtzPreset _preset;

    public string Name => _preset.DisplayName;

    public PresetItemViewModel(ShellViewModel shell, IDeviceClient client, Channel channel, PtzPreset preset)
    {
        _shell = shell;
        _client = client;
        _channel = channel;
        _preset = preset;
    }

    [RelayCommand]
    private Task GotoAsync()
    {
        return _shell.RunAsync(async token =>
        {
            if (_shell.IsCurrentClient(_client))
            {
                await _client.GotoPtzPresetAsync(_channel.Number, _preset, token);
            }
        });
    }
}
