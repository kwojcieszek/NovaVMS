using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Recording segment found by the search.</summary>
public sealed partial class RecordingItemViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly CameraViewModel _camera;
    private readonly RecordingSegment _recording;

    public string Title => $"{_recording.Start:HH:mm:ss} – {_recording.End:HH:mm:ss}";

    public RecordingItemViewModel(ShellViewModel shell, CameraViewModel camera, RecordingSegment recording)
    {
        _shell = shell;
        _camera = camera;
        _recording = recording;
    }

    [RelayCommand]
    private Task PlayAsync()
    {
        return _shell.RunAsync(token => _camera.PlayFromAsync(_recording.Start, token));
    }
}
