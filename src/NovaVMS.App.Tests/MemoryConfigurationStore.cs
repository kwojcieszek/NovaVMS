using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Tests;

/// <summary>Configuration kept in memory; loading returns a snapshot of the last stored configuration.</summary>
internal sealed class MemoryConfigurationStore : IConfigurationStore
{
    public AppConfiguration Stored { get; private set; }

    /// <summary>The last configuration passed to <see cref="SaveAsync"/>; null until something is saved.</summary>
    public AppConfiguration? Saved { get; private set; }

    /// <summary>Makes every save fail as a disk write error would.</summary>
    public bool FailSaves { get; init; }

    public MemoryConfigurationStore(AppConfiguration? initial = null)
    {
        Stored = initial ?? new AppConfiguration();
    }

    public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Stored.CreateSnapshot());
    }

    public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (FailSaves)
        {
            throw new IOException("Test write failure");
        }

        Stored = configuration;
        Saved = configuration;

        return Task.CompletedTask;
    }
}
