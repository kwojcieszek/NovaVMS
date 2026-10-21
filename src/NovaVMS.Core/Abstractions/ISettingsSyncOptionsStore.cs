using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Local storage of the settings database connection.
/// </summary>
public interface ISettingsSyncOptionsStore
{
    Task<SettingsSyncOptions> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SettingsSyncOptions options, CancellationToken cancellationToken = default);
}
