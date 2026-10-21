using System.Collections.ObjectModel;
using NovaVMS.App.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Camera group in the resource tree. Favorites and unassigned cameras have no group identifier.
/// </summary>
public sealed class ResourceGroupNodeViewModel : ResourceNodeViewModel
{
    public ResourceGroupNodeViewModel(ResourceTreeViewModel owner, Guid? id, string name, GroupNodeKind kind)
        : base(owner, name)
    {
        Id = id;
        Kind = kind;
    }

    public Guid? Id { get; }

    /// <summary>
    /// Key under which the live view remembers the split and camera placement of this group.
    /// </summary>
    public string ViewKey => GetViewKey(Id, Kind);

    public static string GetViewKey(Guid? id, GroupNodeKind kind) => id?.ToString() ?? $"special:{kind}";

    public GroupNodeKind Kind { get; }

    public bool IsCustom => Kind == GroupNodeKind.Custom;

    public bool IsDeviceGroup => Kind == GroupNodeKind.Device;

    public bool IsUnassigned => Kind == GroupNodeKind.Unassigned;

    public bool IsFavorites => Kind == GroupNodeKind.Favorites;

    public bool IsViewsFolder => Kind == GroupNodeKind.ViewsFolder;
    public bool IsSavedView => Kind == GroupNodeKind.SavedView;
    public bool IsCameraGroup => !IsViewsFolder && !IsSavedView;
    public bool CanEdit => IsCustom || IsDeviceGroup || IsSavedView;
    public bool CanEditGroup => IsCustom || IsDeviceGroup;
    public ObservableCollection<ResourceGroupNodeViewModel> Views { get; } = new();
    public IEnumerable<ResourceNodeViewModel> Children => IsViewsFolder ? Views : Cameras;

    public ObservableCollection<CameraNodeViewModel> Cameras { get; } = new();
}
