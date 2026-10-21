using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Storage of the settings that belong to this computer only.
/// </summary>
public interface ILocalSettingsStore
{
    /// <summary>
    /// Reads the settings synchronously; they are needed before the main window opens.
    /// </summary>
    LocalSettings Load();

    Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default);
}
