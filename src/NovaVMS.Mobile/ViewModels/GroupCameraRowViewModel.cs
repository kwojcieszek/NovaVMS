using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Camera row of the group editor: a camera in the group or one that can be added.</summary>
public sealed partial class GroupCameraRowViewModel : ViewModelBase
{
    private readonly GroupEditViewModel _owner;
    private readonly MobileCamera _camera;
    private readonly bool _canMoveUp;
    private readonly bool _canMoveDown;

    public string Title => _camera.Title;

    public string Subtitle => _camera.Subtitle;

    public bool IsInGroup { get; }

    public bool IsAvailable => !IsInGroup;

    public GroupCameraRowViewModel(GroupEditViewModel owner, MobileCamera camera, bool isInGroup, bool canMoveUp, bool canMoveDown)
    {
        _owner = owner;
        _camera = camera;
        _canMoveUp = canMoveUp;
        _canMoveDown = canMoveDown;

        IsInGroup = isInGroup;
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
    private Task AddAsync()
    {
        return _owner.AddAsync(_camera);
    }

    [RelayCommand]
    private Task RemoveAsync()
    {
        return _owner.RemoveAsync(_camera);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync()
    {
        return _owner.MoveAsync(_camera, -1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync()
    {
        return _owner.MoveAsync(_camera, 1);
    }
}
