namespace NovaVMS.Core.Models;

/// <summary>
/// Settings of this computer only. Stored apart from the configuration and never sent to the settings
/// database or included in an export.
/// </summary>
public sealed class LocalSettings
{
    public bool UseOptionalProtocol { get; set; }

    /// <summary>
    /// Opens the main window in full screen when the application starts.
    /// </summary>
    public bool StartFullScreen { get; set; }

    /// <summary>
    /// Cameras in one row of the mobile live preview; 0 chooses the count giving the largest pictures.
    /// </summary>
    public int PreviewColumns { get; set; }

    /// <summary>
    /// User logged in without a password when the application starts; empty shows the login window.
    /// </summary>
    public string AutoLoginUserName { get; set; } = string.Empty;

    public LocalSettings Copy() => (LocalSettings)MemberwiseClone();
}
