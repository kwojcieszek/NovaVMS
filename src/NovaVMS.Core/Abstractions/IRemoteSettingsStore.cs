using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Encrypted settings versions shared through a database.
/// </summary>
public interface IRemoteSettingsStore : IDisposable
{
    /// <summary>
    /// Creates missing tables and verifies the encryption password.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<SettingsVersionInfo?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stored versions, newest first.
    /// </summary>
    Task<IReadOnlyList<SettingsVersionInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the configuration as a new version unless it equals the newest one, then deletes versions
    /// beyond <paramref name="retainedVersions"/>.
    /// </summary>
    Task<SettingsUploadResult> UploadAsync(AppConfiguration configuration, Guid instanceId, int retainedVersions, CancellationToken cancellationToken = default);

    Task<AppConfiguration> DownloadAsync(long versionId, CancellationToken cancellationToken = default);
}
