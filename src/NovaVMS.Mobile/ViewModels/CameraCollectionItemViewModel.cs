using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;
using NovaVMS.Presentation.Localization;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Row of the main page that opens the live previews of favorites, a view or a group.</summary>
public sealed partial class CameraCollectionItemViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    public CameraCollection Collection { get; }

    public string Title => Collection.Title;

    public string Subtitle { get; }

    public bool IsFavorites => Collection.Kind == CameraCollectionKind.Favorites;

    public bool IsView => Collection.Kind == CameraCollectionKind.View;

    public bool IsGroup => Collection.Kind == CameraCollectionKind.Group;

    public CameraCollectionItemViewModel(ShellViewModel shell, CameraCollection collection)
    {
        _shell = shell;

        Collection = collection;
        Subtitle = Localizer.Format("Mobile_CameraCount", collection.Resolve(shell.Current).Count);
    }

    [RelayCommand]
    private void Open()
    {
        _shell.ShowCameraCollection(Collection);
    }
}
