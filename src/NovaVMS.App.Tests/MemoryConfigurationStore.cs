using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Tests;

internal sealed class MemoryConfigurationStore(AppConfiguration initial) : IConfigurationStore
{
    public AppConfiguration Stored { get; private set; } = initial;

    public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored.CreateSnapshot());

    public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Stored = configuration;
        return Task.CompletedTask;
    }
}
