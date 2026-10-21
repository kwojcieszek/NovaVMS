using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Tests;

internal sealed class MemorySyncOptionsStore(SettingsSyncOptions options) : ISettingsSyncOptionsStore
{
    public SettingsSyncOptions Options { get; private set; } = options;

    public Task<SettingsSyncOptions> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Options.Copy());

    public Task SaveAsync(SettingsSyncOptions options, CancellationToken cancellationToken = default)
    {
        Options = options.Copy();
        return Task.CompletedTask;
    }
}
