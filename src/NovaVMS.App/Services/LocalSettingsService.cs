using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Settings of this computer only. Unlike <see cref="ConfigurationService"/> they are not part of the
/// configuration, so the settings database synchronization and the export never see them.
/// </summary>
public sealed class LocalSettingsService
{
    private readonly ILocalSettingsStore _store;

    public LocalSettingsService(ILocalSettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    public LocalSettings Current { get; }

    public Task SaveAsync()
    {
        // Serialization runs off the caller's thread; a copy keeps later edits out of it.
        return _store.SaveAsync(Current.Copy());
    }
}
