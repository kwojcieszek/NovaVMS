using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Configuration;
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

    /// <summary>Group created by the user; groups of a recorder are shown with the device icon, as on the desktop.</summary>
    public bool IsCustomGroup { get; }

    public bool IsDeviceGroup => Collection.Kind == CameraCollectionKind.Group && !IsCustomGroup;

    public CameraCollectionItemViewModel(ShellViewModel shell, CameraCollection collection)
    {
        _shell = shell;

        Collection = collection;
        IsCustomGroup = collection.Kind == CameraCollectionKind.Group
            && shell.Current.FindGroup(collection.Id ?? Guid.Empty) is { IsCustom: true };
        Subtitle = Localizer.Format("Mobile_CameraCount", collection.Resolve(shell.Current).Count);
    }

    [RelayCommand]
    private void Open()
    {
        _shell.ShowCameraCollection(Collection);
    }
}
