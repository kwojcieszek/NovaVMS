using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Tests;

internal sealed class MemoryLocalSettingsStore : ILocalSettingsStore
{
    public LocalSettings? Saved { get; private set; }

    public LocalSettings Load() => Saved?.Copy() ?? new LocalSettings();

    public Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default)
    {
        Saved = settings.Copy();
        return Task.CompletedTask;
    }
}
