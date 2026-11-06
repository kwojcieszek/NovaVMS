using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Camera in the resource tree.
/// </summary>
public sealed partial class CameraNodeViewModel : ResourceNodeViewModel
{
    [ObservableProperty]
    private bool _isOnline;

    /// <summary>
    /// The camera or its recorder is known to be offline. Unlike <see cref="IsOnline"/> it stays false
    /// while the availability has not been checked yet.
    /// </summary>
    [ObservableProperty]
    private bool _isUnavailable;

    public CameraReference Reference { get; }

    public string FullName { get; }

    public Guid? ParentGroupId { get; }

    public Guid? ParentViewId { get; init; }

    public int ViewSlotIndex { get; init; }

    public bool CanRemoveFromView => ParentViewId.HasValue;

    public bool IsFavorite { get; init; }

    public string FavoriteActionText => Localizer.Text(IsFavorite ? "Tree_RemoveFavorite" : "Tree_AddFavorite");

    public CameraNodeViewModel(
        ResourceTreeViewModel owner,
        CameraReference reference,
        string name,
        string fullName,
        Guid? parentGroupId)
        : base(owner, name)
    {
        Reference = reference;
        FullName = fullName;
        ParentGroupId = parentGroupId;
    }
}
