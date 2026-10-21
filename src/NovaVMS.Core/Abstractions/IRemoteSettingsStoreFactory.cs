using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Creates the settings database client for the configured server type.
/// </summary>
public interface IRemoteSettingsStoreFactory
{
    IRemoteSettingsStore Create(SettingsSyncOptions options);
}
