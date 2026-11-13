using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Testing;

/// <summary>Settings database connection kept in memory.</summary>
public sealed class MemorySyncOptionsStore : ISettingsSyncOptionsStore
{
    public SettingsSyncOptions Options { get; private set; }

    public MemorySyncOptionsStore(SettingsSyncOptions options)
    {
        Options = options;
    }

    public Task<SettingsSyncOptions> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Options.Copy());
    }

    public Task SaveAsync(SettingsSyncOptions options, CancellationToken cancellationToken = default)
    {
        Options = options.Copy();

        return Task.CompletedTask;
    }
}
