namespace NovaVMS.Infrastructure.Sync;

/// <summary>
/// Names of the tables created in the settings database.
/// </summary>
internal static class SettingsTables
{
    public const string Meta = "novavms_settings_meta";
    public const string Versions = "novavms_settings_versions";

    public const string VersionColumns = "id, created_at, instance_id, machine_name, ip_address, user_name, app_version, content_hash";

    public const string InsertVersionValues =
        "INSERT INTO " + Versions + " (created_at, instance_id, machine_name, ip_address, user_name, app_version, content_hash, payload) "
        + "VALUES (@createdAt, @instanceId, @machineName, @ipAddress, @userName, @appVersion, @contentHash, @payload)";
}
