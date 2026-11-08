namespace NovaVMS.Mobile.Services;

/// <summary>Lets the user choose a settings file with the system file picker.</summary>
public interface IFileService
{
    /// <summary>Returns a writable stream of the chosen file, or null when the user cancelled.</summary>
    Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName);

    /// <summary>Returns a readable stream of the chosen file, or null when the user cancelled.</summary>
    Task<Stream?> OpenJsonFileAsync(string title);
}
