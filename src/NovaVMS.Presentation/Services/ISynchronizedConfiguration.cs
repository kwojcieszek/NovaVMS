using NovaVMS.Core.Models;

namespace NovaVMS.Presentation.Services;

/// <summary>
/// The configuration of an application as seen by the settings database synchronization.
/// </summary>
public interface ISynchronizedConfiguration
{
    AppConfiguration Current { get; }

    /// <summary>
    /// Raised after a local change has been saved; not raised when a configuration from the settings database is applied.
    /// </summary>
    event EventHandler? Saved;

    /// <summary>
    /// Replaces the whole configuration with one received from the settings database and saves it locally.
    /// </summary>
    Task ReplaceAsync(AppConfiguration configuration);
}
