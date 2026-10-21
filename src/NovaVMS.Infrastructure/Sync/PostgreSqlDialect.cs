using System.Data.Common;
using Npgsql;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Sync;

internal sealed class PostgreSqlDialect : ISqlDialect
{
    public DbConnection CreateConnection(SettingsSyncOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host.Trim(),
            Port = options.Port,
            Database = options.Database.Trim(),
            Username = options.UserName.Trim(),
            Password = options.Password,
            Timeout = DatabaseRemoteSettingsStore.ConnectTimeoutSeconds,
            CommandTimeout = DatabaseRemoteSettingsStore.CommandTimeoutSeconds,
            ApplicationName = "NovaVMS"
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }

    public string CreateMetaTable =>
        "CREATE TABLE IF NOT EXISTS " + SettingsTables.Meta + " ("
        + "meta_key VARCHAR(64) PRIMARY KEY, "
        + "meta_value TEXT NOT NULL)";

    public string CreateVersionsTable =>
        "CREATE TABLE IF NOT EXISTS " + SettingsTables.Versions + " ("
        + "id BIGSERIAL PRIMARY KEY, "
        + "created_at TIMESTAMPTZ NOT NULL, "
        + "instance_id VARCHAR(36) NOT NULL, "
        + "machine_name VARCHAR(255) NOT NULL, "
        + "ip_address VARCHAR(64) NOT NULL, "
        + "user_name VARCHAR(255) NOT NULL, "
        + "app_version VARCHAR(64) NOT NULL, "
        + "content_hash VARCHAR(64) NOT NULL, "
        + "payload BYTEA NOT NULL)";

    public string InsertMetaIfMissing =>
        "INSERT INTO " + SettingsTables.Meta + " (meta_key, meta_value) VALUES (@key, @value) ON CONFLICT (meta_key) DO NOTHING";

    public string InsertVersion => SettingsTables.InsertVersionValues + " RETURNING id";
}
