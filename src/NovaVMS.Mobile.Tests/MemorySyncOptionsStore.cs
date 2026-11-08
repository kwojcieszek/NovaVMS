using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Tests;

/// <summary>Settings database connection kept in memory.</summary>
internal sealed class MemorySyncOptionsStore(SettingsSyncOptions options) : ISettingsSyncOptionsStore
{
    public SettingsSyncOptions Options { get; private set; } = options;

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
