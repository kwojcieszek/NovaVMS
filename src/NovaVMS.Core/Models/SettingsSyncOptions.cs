namespace NovaVMS.Core.Models;

/// <summary>
/// Connection to the shared settings database. Stored only on this computer and never sent to the database.
/// </summary>
public sealed class SettingsSyncOptions
{
    public const int DefaultCheckIntervalMinutes = 30;
    public const int DefaultRetainedVersions = 20;
    public const int DefaultPostgreSqlPort = 5432;
    public const int DefaultMySqlPort = 3306;

    public SettingsSyncOptions Copy() => (SettingsSyncOptions)MemberwiseClone();

    public bool Enabled { get; set; }

    public SettingsDatabaseType DatabaseType { get; set; } = SettingsDatabaseType.PostgreSql;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = DefaultPostgreSqlPort;

    public string Database { get; set; } = "novavms";

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Password the settings are encrypted with; every computer sharing the database must use the same one.
    /// </summary>
    public string EncryptionPassword { get; set; } = string.Empty;

    /// <summary>
    /// Periodically checks for a newer settings version and applies it.
    /// </summary>
    public bool AutoCheck { get; set; } = true;

    public int CheckIntervalMinutes { get; set; } = DefaultCheckIntervalMinutes;

    /// <summary>
    /// Sends the settings to the database after every local change.
    /// </summary>
    public bool AutoUpload { get; set; }

    /// <summary>
    /// Number of most recent versions kept in the database; older ones are deleted.
    /// </summary>
    public int RetainedVersions { get; set; } = DefaultRetainedVersions;

    /// <summary>
    /// Identifies this installation, so versions sent from here are not applied back.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Newest database version already present on this computer (applied or sent from here).
    /// </summary>
    public long LastKnownVersionId { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Database)
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrEmpty(EncryptionPassword);

    /// <summary>
    /// True when both options point to the same database.
    /// </summary>
    public bool HasSameTarget(SettingsSyncOptions other)
    {
        return DatabaseType == other.DatabaseType
            && Port == other.Port
            && string.Equals(Host.Trim(), other.Host.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Database.Trim(), other.Database.Trim(), StringComparison.Ordinal);
    }
}
