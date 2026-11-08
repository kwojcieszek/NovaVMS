using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Live previews of every camera of favorites, a saved view or a group: sub stream without audio,
/// frame limit and cheaper decoding chosen by <see cref="PlaybackPolicy"/> for the number of previews.
/// </summary>
public sealed partial class CameraGroupViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _starts = new(PlaybackPolicy.ConcurrentPreviewStarts, PlaybackPolicy.ConcurrentPreviewStarts);
    private bool _released;

    /// <summary>Camera names on the tiles; the synchronized "show titles" setting of the desktop live view.</summary>
    [ObservableProperty]
    private bool _showTitles;

    public override MobileTab Tab => MobileTab.Cameras;

    public CameraCollection Collection { get; }

    public string Title => Collection.Title;

    public IReadOnlyList<CameraTileViewModel> Tiles { get; }

    public bool IsEmpty => Tiles.Count == 0;

    public CameraGroupViewModel(ShellViewModel shell, CameraCollection collection)
    {
        _shell = shell;
        _showTitles = shell.Current.Settings.ShowTileTitles;

        Collection = collection;
        Tiles = collection.Resolve(shell.Current)
            .Select(camera => new CameraTileViewModel(shell, camera, collection))
            .ToList();

        for (var index = 0; index < Tiles.Count; index++)
        {
            var options = PlaybackPolicy.ForPreview(shell.Current.Settings, Tiles.Count, index);

            _ = Tiles[index].StartAsync(options, _starts, _lifetime.Token);
        }
    }

    /// <summary>Stops the previews and disposes their players; called before the page is left.</summary>
    public async Task ReleaseAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _lifetime.Cancel();

        await Task.WhenAll(Tiles.Select(tile => tile.ReleaseAsync()));

        _lifetime.Dispose();
    }

    [RelayCommand]
    private async Task ToggleTitlesAsync()
    {
        ShowTitles = !ShowTitles;
        _shell.Current.Settings.ShowTileTitles = ShowTitles;

        await _shell.SaveConfigurationAsync();
    }
}
