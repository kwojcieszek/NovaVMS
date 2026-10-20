namespace NovaVMS.App.Services;

/// <summary>
/// Dialogs shown on top of the currently active window.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Asks for a line of text. Returns null when the user cancels.
    /// </summary>
    Task<string?> PromptTextAsync(string title, string message, string initialValue);

    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>
    /// Lets the user pick a JSON file to read. Returns null when the user cancels; the caller disposes the stream.
    /// </summary>
    Task<Stream?> OpenJsonFileAsync(string title);

    /// <summary>
    /// Lets the user choose where to save a JSON file. Returns null when the user cancels; the caller disposes the stream.
    /// </summary>
    Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName);

    /// <summary>
    /// Lets the user pick a CSV file to read. Returns null when the user cancels; the caller disposes the stream.
    /// </summary>
    Task<Stream?> OpenCsvFileAsync(string title);

    /// <summary>
    /// Opens an iVMS ZIP backup or a Device file with its DESetup.xml companion.
    /// The caller disposes both streams. Returns null when the user cancels.
    /// </summary>
    Task<(Stream Content, Stream? Setup)?> OpenIvmsFileAsync(string title);

    /// <summary>
    /// Lets the user choose where to save a CSV file. Returns null when the user cancels; the caller disposes the stream.
    /// </summary>
    Task<Stream?> SaveCsvFileAsync(string title, string suggestedFileName);

    /// <summary>
    /// Lets the user pick a folder. Returns its local path, or null when the user cancels.
    /// </summary>
    Task<string?> PickFolderAsync(string title);
}
