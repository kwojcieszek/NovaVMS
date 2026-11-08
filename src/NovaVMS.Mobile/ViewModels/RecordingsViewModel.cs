using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Recording search for one day in the recorder's local time.</summary>
public sealed partial class RecordingsViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly CameraViewModel _camera;
    private readonly IDeviceClient _client;
    private readonly Channel _channel;

    [ObservableProperty]
    private DateTime? _selectedDate = DateTime.Today;

    public ObservableCollection<RecordingItemViewModel> Results { get; } = new();

    public RecordingsViewModel(ShellViewModel shell, CameraViewModel camera, IDeviceClient client, Channel channel)
    {
        _shell = shell;
        _camera = camera;
        _client = client;
        _channel = channel;
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        return _shell.RunAsync(async token =>
        {
            if (!_shell.IsCurrentClient(_client) || SelectedDate is not { } day)
            {
                return;
            }

            var recordings = await _client.SearchRecordingsAsync(_channel.Number, day.Date, day.Date.AddDays(1), token);

            token.ThrowIfCancellationRequested();
            Results.Clear();

            foreach (var recording in recordings.OrderBy(r => r.Start))
            {
                Results.Add(new RecordingItemViewModel(_shell, _camera, recording));
            }

            _shell.Status = recordings.Count == 0
                ? Localizer.Text("Mobile_NoRecordings")
                : Localizer.Format("Mobile_RecordingsFound", recordings.Count);
        });
    }
}
