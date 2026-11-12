using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Live previews of every camera of favorites, a saved view or a group: sub stream without audio, frame limit and
/// cheaper decoding chosen by <see cref="PlaybackPolicy"/> for the number of previews. Automatically every camera fits
/// on the screen; with a chosen number of cameras per row the 16:9 tiles are scrolled by touch.
/// </summary>
public sealed partial class CameraGroupViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _starts = new(PlaybackPolicy.ConcurrentPreviewStarts, PlaybackPolicy.ConcurrentPreviewStarts);
    private readonly SemaphoreSlim _playback = new(1, 1);
    private CancellationTokenSource? _runLifetime;
    private bool _released;

    /// <summary>Camera names on the tiles; the synchronized "show titles" setting of the desktop live view.</summary>
    [ObservableProperty]
    private bool _showTitles;

    /// <summary>Cameras in one row; a setting of this phone, kept in the local settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScrollable))]
    private PreviewColumnsOption _columns;

    public override MobileTab Tab => MobileTab.Cameras;

    public CameraCollection Collection { get; }

    public string Title => Collection.Title;

    public IReadOnlyList<CameraTileViewModel> Tiles { get; }

    public IReadOnlyList<PreviewColumnsOption> ColumnOptions => PreviewColumnsOption.All;

    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>With a chosen number of cameras per row the tiles keep 16:9 proportions and the list scrolls.</summary>
    public bool IsScrollable => !Columns.IsAutomatic;

    public CameraGroupViewModel(ShellViewModel shell, CameraCollection collection)
    {
        _shell = shell;
        _showTitles = shell.Current.Settings.ShowTileTitles;
        _columns = PreviewColumnsOption.Find(shell.LocalSettings.PreviewColumns);

        Collection = collection;
        Tiles = collection.Resolve(shell.Current)
            .Select(camera => new CameraTileViewModel(shell, camera, collection))
            .ToList();

        StartPreviews();
    }

    /// <summary>
    /// Stops the previews while an Avalonia pop-up is open: native video views would be drawn above it.
    /// </summary>
    public async Task PausePreviewsAsync()
    {
        await _playback.WaitAsync();

        try
        {
            await StopPreviewsAsync();
        }
        finally
        {
            _playback.Release();
        }
    }

    /// <summary>Starts the paused previews again.</summary>
    public async Task ResumePreviewsAsync()
    {
        await _playback.WaitAsync();

        try
        {
            if (!_released && _runLifetime is null)
            {
                StartPreviews();
            }
        }
        finally
        {
            _playback.Release();
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

        await _playback.WaitAsync();

        try
        {
            await StopPreviewsAsync();
            await Task.WhenAll(Tiles.Select(tile => tile.ReleaseAsync()));
        }
        finally
        {
            _playback.Release();
        }

        _lifetime.Dispose();
    }

    partial void OnColumnsChanged(PreviewColumnsOption value)
    {
        _shell.LocalSettings.PreviewColumns = value.Value;

        _ = _shell.SaveLocalSettingsAsync();
    }

    [RelayCommand]
    private async Task ToggleTitlesAsync()
    {
        ShowTitles = !ShowTitles;
        _shell.Current.Settings.ShowTileTitles = ShowTitles;

        await _shell.SaveConfigurationAsync();
    }

    private void StartPreviews()
    {
        _runLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        for (var index = 0; index < Tiles.Count; index++)
        {
            var options = PlaybackPolicy.ForPreview(_shell.Current.Settings, Tiles.Count, index);

            _ = Tiles[index].StartAsync(options, _starts, _runLifetime.Token);
        }
    }

    private async Task StopPreviewsAsync()
    {
        if (_runLifetime is not { } runLifetime)
        {
            return;
        }

        _runLifetime = null;

        runLifetime.Cancel();

        await Task.WhenAll(Tiles.Select(tile => tile.StopAsync()));

        runLifetime.Dispose();
    }
}
