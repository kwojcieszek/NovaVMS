using System.Collections.ObjectModel;
using NovaVMS.App.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Camera group in the resource tree. The unassigned group has no identifier.
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

    public bool CanEdit => Kind != GroupNodeKind.Unassigned;

    public ObservableCollection<CameraNodeViewModel> Cameras { get; } = new();
}
