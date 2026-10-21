using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Entry of the settings database server list.
/// </summary>
public sealed class DatabaseTypeOption
{
    private DatabaseTypeOption(SettingsDatabaseType value, string label, int defaultPort)
    {
        Value = value;
        Label = label;
        DefaultPort = defaultPort;
    }

    public static IReadOnlyList<DatabaseTypeOption> All { get; } = new List<DatabaseTypeOption>
    {
        new(SettingsDatabaseType.PostgreSql, "PostgreSQL", SettingsSyncOptions.DefaultPostgreSqlPort),
        new(SettingsDatabaseType.MySql, "MySQL / MariaDB", SettingsSyncOptions.DefaultMySqlPort)
    };

    public SettingsDatabaseType Value { get; }

    public string Label { get; }

    public int DefaultPort { get; }

    public static DatabaseTypeOption Find(SettingsDatabaseType value)
    {
        return All.FirstOrDefault(option => option.Value == value) ?? All[0];
    }
}
