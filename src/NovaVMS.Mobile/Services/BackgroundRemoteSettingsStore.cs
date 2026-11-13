using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Runs every settings database request on the thread pool: connecting, encryption and serialization
/// must not block the interface thread.
/// </summary>
public sealed class BackgroundRemoteSettingsStoreFactory : IRemoteSettingsStoreFactory
{
    private readonly IRemoteSettingsStoreFactory _inner;

    public BackgroundRemoteSettingsStoreFactory(IRemoteSettingsStoreFactory inner)
    {
        _inner = inner;
    }

    public IRemoteSettingsStore Create(SettingsSyncOptions options)
    {
        return new BackgroundRemoteSettingsStore(_inner.Create(options));
    }

    private sealed class BackgroundRemoteSettingsStore : IRemoteSettingsStore
    {
        private readonly IRemoteSettingsStore _store;

        public BackgroundRemoteSettingsStore(IRemoteSettingsStore store)
        {
            _store = store;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => _store.InitializeAsync(cancellationToken), cancellationToken);
        }

        public Task<SettingsVersionInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => _store.GetLatestAsync(cancellationToken), cancellationToken);
        }

        public Task<IReadOnlyList<SettingsVersionInfo>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => _store.ListAsync(cancellationToken), cancellationToken);
        }

        public Task<SettingsUploadResult> UploadAsync(AppConfiguration configuration, Guid instanceId, int retainedVersions, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => _store.UploadAsync(configuration, instanceId, retainedVersions, cancellationToken), cancellationToken);
        }

        public Task<AppConfiguration> DownloadAsync(long versionId, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => _store.DownloadAsync(versionId, cancellationToken), cancellationToken);
        }

        public void Dispose()
        {
            _store.Dispose();
        }
    }
}
