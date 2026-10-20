using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Camera in the resource tree.
/// </summary>
public sealed partial class CameraNodeViewModel : ResourceNodeViewModel
{
    [ObservableProperty]
    private bool _isOnline;

    public CameraNodeViewModel(
        ResourceTreeViewModel owner,
        CameraReference reference,
        string name,
        string fullName,
        Guid? parentGroupId,
        bool canRemoveFromGroup,
        IReadOnlyList<MenuActionViewModel> addToGroupActions)
        : base(owner, name)
    {
        Reference = reference;
        FullName = fullName;
        ParentGroupId = parentGroupId;
        CanRemoveFromGroup = canRemoveFromGroup;
        AddToGroupActions = addToGroupActions;
    }

    public CameraReference Reference { get; }

    public string FullName { get; }

    public Guid? ParentGroupId { get; }

    public bool CanRemoveFromGroup { get; }

    public IReadOnlyList<MenuActionViewModel> AddToGroupActions { get; }

    public bool HasAddToGroupActions => AddToGroupActions.Count > 0;
}
