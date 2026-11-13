using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Models;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Camera row of the search results.</summary>
public sealed partial class CameraItemViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly Device _device;
    private readonly Channel _channel;
    private readonly CameraCollection? _returnTo;

    public string Title => _channel.DisplayName;

    public string Subtitle => _device.DisplayName;

    public bool IsFavorite => _channel.IsFavorite;

    public bool IsRegular => !_channel.IsFavorite;

    public string AutomationName => $"{(IsFavorite ? "★ " : "")}{Title}\n{Subtitle}";

    public CameraItemViewModel(ShellViewModel shell, Device device, Channel channel, CameraCollection? returnTo)
    {
        _shell = shell;
        _device = device;
        _channel = channel;
        _returnTo = returnTo;
    }

    [RelayCommand]
    private Task OpenAsync()
    {
        return _shell.GuardAsync(() => _shell.OpenCameraAsync(_device, _channel, _returnTo));
    }
}
