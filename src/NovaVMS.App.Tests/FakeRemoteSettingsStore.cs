using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Tests;

/// <summary>
/// In-memory settings database used by the synchronization tests; it is its own factory.
/// </summary>
internal sealed class FakeRemoteSettingsStore : IRemoteSettingsStore, IRemoteSettingsStoreFactory
{
    private readonly List<(SettingsVersionInfo Info, AppConfiguration Configuration)> _versions = new();

    public IRemoteSettingsStore Create(SettingsSyncOptions options) => this;

    public SettingsVersionInfo Add(AppConfiguration configuration, Guid instanceId, string machineName = "OTHER-PC")
    {
        var info = new SettingsVersionInfo(_versions.Count + 1, DateTimeOffset.UtcNow, instanceId, machineName, "10.0.0.9", "user", "0.1.0", Guid.NewGuid().ToString("N"));
        _versions.Add((info, configuration));
        return info;
    }

    public int Count => _versions.Count;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SettingsVersionInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_versions.Count == 0 ? null : _versions[^1].Info);
    }

    public Task<IReadOnlyList<SettingsVersionInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SettingsVersionInfo>>(_versions.Select(version => version.Info).Reverse().ToList());
    }

    public Task<SettingsUploadResult> UploadAsync(AppConfiguration configuration, Guid instanceId, int retainedVersions, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SettingsUploadResult(Add(configuration, instanceId, Environment.MachineName), true));
    }

    public Task<AppConfiguration> DownloadAsync(long versionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_versions.Single(version => version.Info.Id == versionId).Configuration.CreateSnapshot());
    }

    public void Dispose()
    {
    }
}
