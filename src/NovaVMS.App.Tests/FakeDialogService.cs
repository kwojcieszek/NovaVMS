using NovaVMS.App.Services;

namespace NovaVMS.App.Tests;

/// <summary>
/// Dialogs answered by the test. Confirmations and folder picks are counted; text prompts and folder picks
/// return the configured reply (null means cancelled). File dialogs and the login are not expected.
/// </summary>
internal sealed class FakeDialogService : IDialogService
{
    public Task<bool> ConfirmReply { get; init; } = Task.FromResult(false);

    public string? PromptReply { get; init; }

    public string? FolderReply { get; init; }

    public TaskCompletionSource ConfirmShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Confirmations { get; private set; }

    public int FolderPicks { get; private set; }

    public Task<string?> PromptTextAsync(string title, string message, string initialValue)
    {
        return Task.FromResult(PromptReply);
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Confirmations++;

        ConfirmShown.TrySetResult();

        return ConfirmReply;
    }

    public Task<string?> PickFolderAsync(string title)
    {
        FolderPicks++;

        return Task.FromResult(FolderReply);
    }

    public Task<bool> LoginAsync(UserSessionService session, string userName)
    {
        throw new NotSupportedException();
    }

    public Task<Stream?> OpenJsonFileAsync(string title)
    {
        throw new NotSupportedException();
    }

    public Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName)
    {
        throw new NotSupportedException();
    }

    public Task<Stream?> OpenCsvFileAsync(string title)
    {
        throw new NotSupportedException();
    }

    public Task<(Stream Content, Stream? Setup)?> OpenIvmsFileAsync(string title)
    {
        throw new NotSupportedException();
    }

    public Task<Stream?> SaveCsvFileAsync(string title, string suggestedFileName)
    {
        throw new NotSupportedException();
    }
}
