using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

public sealed partial class PlaybackTileViewModel : ViewModelBase
{
    private readonly PlaybackWorkspaceViewModel _workspace;
    public PlaybackViewModel Playback { get; }
    public int Number { get; }
    public CameraReference? Camera { get; internal set; }
    public string Header => $"{Number}. {Playback.CameraName}";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExportSelected = true;

    partial void OnIsExportSelectedChanged(bool value) => _workspace.RefreshExportSelection();

    public PlaybackTileViewModel(int number, PlaybackViewModel playback, PlaybackWorkspaceViewModel workspace)
    {
        Number = number;
        Playback = playback;
        _workspace = workspace;
        Playback.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PlaybackViewModel.CameraName)) OnPropertyChanged(nameof(Header));
        };
    }

    [RelayCommand]
    private void Select() => _workspace.SelectTile(this);

    [RelayCommand]
    private void Close() => _workspace.ClearCamera(this);
}
