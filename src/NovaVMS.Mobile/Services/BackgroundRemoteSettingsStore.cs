using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Runs every settings database request on the thread pool: connecting, encryption and serialization
/// must not block the interface thread.
/// </summary>
public sealed class BackgroundRemoteSettingsStoreFactory(IRemoteSettingsStoreFactory inner) : IRemoteSettingsStoreFactory
{
    public IRemoteSettingsStore Create(SettingsSyncOptions options)
    {
        return new BackgroundRemoteSettingsStore(inner.Create(options));
    }

    private sealed class BackgroundRemoteSettingsStore(IRemoteSettingsStore store) : IRemoteSettingsStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.Run(() => store.InitializeAsync(cancellationToken), cancellationToken);

        public Task<SettingsVersionInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
            => Task.Run(() => store.GetLatestAsync(cancellationToken), cancellationToken);

        public Task<IReadOnlyList<SettingsVersionInfo>> ListAsync(CancellationToken cancellationToken = default)
            => Task.Run(() => store.ListAsync(cancellationToken), cancellationToken);

        public Task<SettingsUploadResult> UploadAsync(AppConfiguration configuration, Guid instanceId, int retainedVersions, CancellationToken cancellationToken = default)
            => Task.Run(() => store.UploadAsync(configuration, instanceId, retainedVersions, cancellationToken), cancellationToken);

        public Task<AppConfiguration> DownloadAsync(long versionId, CancellationToken cancellationToken = default)
            => Task.Run(() => store.DownloadAsync(versionId, cancellationToken), cancellationToken);

        public void Dispose()
        {
            store.Dispose();
        }
    }
}
