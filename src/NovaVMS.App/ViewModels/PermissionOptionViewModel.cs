using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// One permission checkbox in the user editor.
/// </summary>
public sealed partial class PermissionOptionViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isChecked;

    public PermissionOptionViewModel(UserPermissions permission)
    {
        Permission = permission;
    }

    public UserPermissions Permission { get; }

    public string Label => Localizer.Text($"Permission_{Permission}");

    public void NotifyLabelChanged() => OnPropertyChanged(nameof(Label));
}
