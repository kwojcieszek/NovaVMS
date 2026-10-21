using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Sync;

public sealed class RemoteSettingsStoreFactory : IRemoteSettingsStoreFactory
{
    private readonly IConfigurationSerializer _serializer;

    public RemoteSettingsStoreFactory(IConfigurationSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;
    }

    public IRemoteSettingsStore Create(SettingsSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ISqlDialect dialect = options.DatabaseType switch
        {
            SettingsDatabaseType.PostgreSql => new PostgreSqlDialect(),
            SettingsDatabaseType.MySql => new MySqlDialect(),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.DatabaseType, "Unsupported settings database type.")
        };

        return new DatabaseRemoteSettingsStore(dialect, options, _serializer);
    }
}
