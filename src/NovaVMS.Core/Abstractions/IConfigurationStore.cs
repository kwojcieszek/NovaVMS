using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Persistent storage of the application configuration.
/// </summary>
public interface IConfigurationStore
{
    Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
}
