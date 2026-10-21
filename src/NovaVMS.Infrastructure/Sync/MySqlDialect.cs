using System.Data.Common;
using MySqlConnector;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Sync;

internal sealed class MySqlDialect : ISqlDialect
{
    public DbConnection CreateConnection(SettingsSyncOptions options)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Host.Trim(),
            Port = checked((uint)options.Port),
            Database = options.Database.Trim(),
            UserID = options.UserName.Trim(),
            Password = options.Password,
            ConnectionTimeout = DatabaseRemoteSettingsStore.ConnectTimeoutSeconds,
            DefaultCommandTimeout = DatabaseRemoteSettingsStore.CommandTimeoutSeconds,
            ApplicationName = "NovaVMS"
        };

        return new MySqlConnection(builder.ConnectionString);
    }

    public string CreateMetaTable =>
        "CREATE TABLE IF NOT EXISTS " + SettingsTables.Meta + " ("
        + "meta_key VARCHAR(64) NOT NULL PRIMARY KEY, "
        + "meta_value TEXT NOT NULL"
        + ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";

    public string CreateVersionsTable =>
        "CREATE TABLE IF NOT EXISTS " + SettingsTables.Versions + " ("
        + "id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, "
        + "created_at DATETIME(6) NOT NULL, "
        + "instance_id VARCHAR(36) NOT NULL, "
        + "machine_name VARCHAR(255) NOT NULL, "
        + "ip_address VARCHAR(64) NOT NULL, "
        + "user_name VARCHAR(255) NOT NULL, "
        + "app_version VARCHAR(64) NOT NULL, "
        + "content_hash VARCHAR(64) NOT NULL, "
        + "payload LONGBLOB NOT NULL"
        + ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";

    public string InsertMetaIfMissing =>
        "INSERT IGNORE INTO " + SettingsTables.Meta + " (meta_key, meta_value) VALUES (@key, @value)";

    public string InsertVersion => SettingsTables.InsertVersionValues + "; SELECT LAST_INSERT_ID()";
}
