using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Main page, structured like the desktop resource tree: favorites, saved views and camera groups.
/// Searching lists matching cameras directly.
/// </summary>
public sealed partial class CamerasViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowsing))]
    private bool _isSearching;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasViews;

    [ObservableProperty]
    private bool _hasGroups;

    [ObservableProperty]
    private bool _hasNoResults;

    public override MobileTab Tab => MobileTab.Cameras;

    public override bool IsRoot => true;

    public bool IsBrowsing => !IsSearching;

    public bool ShowSearch => _shell.Current.Settings.ShowCameraSearch;

    public bool HasNoCameras => _shell.Current.Devices.All(device => device.Channels.Count == 0);

    public CameraCollectionItemViewModel Favorites { get; }

    public ObservableCollection<CameraCollectionItemViewModel> Views { get; } = new();

    public ObservableCollection<CameraCollectionItemViewModel> Groups { get; } = new();

    public ObservableCollection<CameraItemViewModel> SearchResults { get; } = new();

    public CamerasViewModel(ShellViewModel shell)
    {
        _shell = shell;

        var configuration = shell.Current;

        Favorites = new CameraCollectionItemViewModel(shell, CameraCollection.Favorites(Localizer.Text("Tree_Favorites")));

        foreach (var view in configuration.Views)
        {
            Views.Add(new CameraCollectionItemViewModel(shell, CameraCollection.ForView(view)));
        }

        foreach (var group in configuration.Groups)
        {
            Groups.Add(new CameraCollectionItemViewModel(shell, CameraCollection.ForGroup(group)));
        }

        _hasViews = Views.Count > 0;
        _hasGroups = Groups.Count > 0;
    }

    partial void OnSearchTextChanged(string value)
    {
        SearchResults.Clear();

        var query = value.Trim();

        IsSearching = query.Length > 0;

        if (!IsSearching)
        {
            HasNoResults = false;

            return;
        }

        foreach (var device in _shell.Current.Devices)
        {
            foreach (var channel in device.Channels.Where(c => c.Enabled).OrderByDescending(c => c.IsFavorite))
            {
                if ($"{device.DisplayName} {channel.DisplayName}".Contains(query, StringComparison.CurrentCultureIgnoreCase))
                {
                    SearchResults.Add(new CameraItemViewModel(_shell, device, channel, null));
                }
            }
        }

        HasNoResults = SearchResults.Count == 0;
    }
}
