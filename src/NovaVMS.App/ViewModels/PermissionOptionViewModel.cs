using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// One permission checkbox in the user editor.
/// </summary>
public sealed partial class PermissionOptionViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isChecked;

    public UserPermissions Permission { get; }

    public string Label => Localizer.Text($"Permission_{Permission}");

    public PermissionOptionViewModel(UserPermissions permission)
    {
        Permission = permission;
    }

    public void NotifyLabelChanged()
    {
        OnPropertyChanged(nameof(Label));
    }
}
