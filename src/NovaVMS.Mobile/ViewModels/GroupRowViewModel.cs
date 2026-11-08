using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Group row of the group settings list.</summary>
public sealed partial class GroupRowViewModel : ViewModelBase
{
    private readonly GroupsSettingsViewModel _owner;
    private readonly CameraGroup _group;
    private readonly bool _canMoveUp;
    private readonly bool _canMoveDown;

    public string Name => _group.Name;

    public string Subtitle => Localizer.Format("Mobile_CameraCount", _group.Cameras.Count);

    public bool IsCustom => _group.IsCustom;

    public bool IsDeviceGroup => !_group.IsCustom;

    public GroupRowViewModel(GroupsSettingsViewModel owner, CameraGroup group, bool canMoveUp, bool canMoveDown)
    {
        _owner = owner;
        _group = group;
        _canMoveUp = canMoveUp;
        _canMoveDown = canMoveDown;
    }

    private bool CanMoveUp()
    {
        return _canMoveUp;
    }

    private bool CanMoveDown()
    {
        return _canMoveDown;
    }

    [RelayCommand]
    private void Edit()
    {
        _owner.Edit(_group);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync()
    {
        return _owner.MoveAsync(_group, -1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync()
    {
        return _owner.MoveAsync(_group, 1);
    }
}
