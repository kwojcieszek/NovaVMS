using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Infrastructure.Sync;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

/// <summary>
/// Runs against real servers only when NOVAVMS_TEST_POSTGRES / NOVAVMS_TEST_MYSQL hold
/// "host;port;database;user;password". The settings tables in that database are dropped.
/// </summary>
public sealed class DatabaseRemoteSettingsStoreTests
{
    public static TheoryData<SettingsDatabaseType> DatabaseTypes => new()
    {
        SettingsDatabaseType.PostgreSql,
        SettingsDatabaseType.MySql
    };

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public async Task CreatesTablesAndRoundTripsEncryptedVersions(SettingsDatabaseType type)
    {
        var options = await PrepareAsync(type);
        if (options is null) return;
        var instanceId = Guid.NewGuid();
        using var store = Create(options);

        await store.InitializeAsync();
        Assert.Null(await store.GetLatestAsync());

        var configuration = Configuration("first", "unique-password-marker");
        var first = await store.UploadAsync(configuration, instanceId, 10);
        var unchanged = await store.UploadAsync(configuration, instanceId, 10);
        var second = await store.UploadAsync(Configuration("second", "other-password"), instanceId, 10);

        Assert.True(first.Created);
        Assert.False(unchanged.Created);
        Assert.Equal(first.Version.Id, unchanged.Version.Id);
        Assert.True(second.Created);
        Assert.True(second.Version.Id > first.Version.Id);
        Assert.Equal(instanceId, second.Version.InstanceId);
        Assert.Equal(Environment.MachineName, second.Version.MachineName);

        var versions = await store.ListAsync();
        Assert.Equal([second.Version.Id, first.Version.Id], versions.Select(version => version.Id));
        Assert.Equal(second.Version.Id, (await store.GetLatestAsync())?.Id);
        Assert.True(Math.Abs((versions[0].CreatedAt - DateTimeOffset.UtcNow).TotalMinutes) < 5);

        var archived = await store.DownloadAsync(first.Version.Id);
        var device = Assert.Single(archived.Devices);
        Assert.Equal("first", device.Name);
        Assert.Equal("unique-password-marker", device.Password);
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public async Task KeepsOnlyRetainedVersions(SettingsDatabaseType type)
    {
        var options = await PrepareAsync(type);
        if (options is null) return;
        using var store = Create(options);

        for (var index = 0; index < 5; index++)
        {
            await store.UploadAsync(Configuration("camera " + index, "password"), Guid.NewGuid(), 3);
        }

        var versions = await store.ListAsync();
        Assert.Equal(3, versions.Count);
        Assert.Equal("camera 4", Assert.Single((await store.DownloadAsync(versions[0].Id)).Devices).Name);
        Assert.Equal("camera 2", Assert.Single((await store.DownloadAsync(versions[2].Id)).Devices).Name);
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public async Task WrongEncryptionPasswordIsRejected(SettingsDatabaseType type)
    {
        var options = await PrepareAsync(type);
        if (options is null) return;
        using (var store = Create(options))
        {
            await store.UploadAsync(Configuration("camera", "password"), Guid.NewGuid(), 10);
        }

        var wrong = options.Copy();
        wrong.EncryptionPassword = "wrong-password";
        using var other = Create(wrong);

        var exception = await Assert.ThrowsAsync<SettingsSyncException>(() => other.InitializeAsync());
        Assert.Equal(SettingsSyncFailure.WrongEncryptionPassword, exception.Failure);
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public async Task MissingVersionIsReported(SettingsDatabaseType type)
    {
        var options = await PrepareAsync(type);
        if (options is null) return;
        using var store = Create(options);

        var exception = await Assert.ThrowsAsync<SettingsSyncException>(() => store.DownloadAsync(123456));
        Assert.Equal(SettingsSyncFailure.VersionNotFound, exception.Failure);
    }

    [Fact]
    public async Task UnreachableServerIsReportedAsConnectionFailure()
    {
        var options = new SettingsSyncOptions
        {
            DatabaseType = SettingsDatabaseType.PostgreSql,
            Host = "127.0.0.1",
            Port = 1,
            UserName = "nobody",
            EncryptionPassword = "password"
        };
        using var store = Create(options);

        var exception = await Assert.ThrowsAsync<SettingsSyncException>(() => store.InitializeAsync());
        Assert.Equal(SettingsSyncFailure.ConnectionFailed, exception.Failure);
    }

    private static DatabaseRemoteSettingsStore Create(SettingsSyncOptions options)
    {
        ISqlDialect dialect = options.DatabaseType == SettingsDatabaseType.MySql ? new MySqlDialect() : new PostgreSqlDialect();
        return new DatabaseRemoteSettingsStore(dialect, options, new JsonConfigurationSerializer());
    }

    private static AppConfiguration Configuration(string deviceName, string password) => new()
    {
        Devices = [new Device { Name = deviceName, Host = "192.168.1.10", Password = password }]
    };

    private static async Task<SettingsSyncOptions?> PrepareAsync(SettingsDatabaseType type)
    {
        var variable = type == SettingsDatabaseType.MySql ? "NOVAVMS_TEST_MYSQL" : "NOVAVMS_TEST_POSTGRES";
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(';');
        var options = new SettingsSyncOptions
        {
            Enabled = true,
            DatabaseType = type,
            Host = parts[0],
            Port = int.Parse(parts[1]),
            Database = parts[2],
            UserName = parts[3],
            Password = parts[4],
            EncryptionPassword = "shared-test-password"
        };

        ISqlDialect dialect = type == SettingsDatabaseType.MySql ? new MySqlDialect() : new PostgreSqlDialect();
        await using var connection = dialect.CreateConnection(options);
        await connection.OpenAsync();
        foreach (var table in new[] { SettingsTables.Versions, SettingsTables.Meta })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE IF EXISTS " + table;
            await command.ExecuteNonQueryAsync();
        }

        return options;
    }
}
