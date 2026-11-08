using CommunityToolkit.Mvvm.Input;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Question shown over the current page; the mobile counterpart of the desktop confirmation dialog.</summary>
public sealed partial class ConfirmationViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Title { get; }

    public string Message { get; }

    public Task<bool> Result => _result.Task;

    public ConfirmationViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    /// <summary>Closes the question as declined, e.g. when the application goes to the background.</summary>
    public void Cancel()
    {
        _result.TrySetResult(false);
    }

    [RelayCommand]
    private void Accept()
    {
        _result.TrySetResult(true);
    }

    [RelayCommand]
    private void Decline()
    {
        _result.TrySetResult(false);
    }
}
