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

    public GroupNodeKind Kind { get; }

    public bool IsCustom => Kind == GroupNodeKind.Custom;

    public bool IsDeviceGroup => Kind == GroupNodeKind.Device;

    public bool IsUnassigned => Kind == GroupNodeKind.Unassigned;

    public bool IsFavorites => Kind == GroupNodeKind.Favorites;

    public bool CanEdit => IsCustom || IsDeviceGroup;

    public ObservableCollection<CameraNodeViewModel> Cameras { get; } = new();
}
