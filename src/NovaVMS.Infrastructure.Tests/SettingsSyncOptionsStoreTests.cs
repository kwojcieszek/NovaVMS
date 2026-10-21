using System.Text;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class SettingsSyncOptionsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-sync-options-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "sync.json");

    [Fact]
    public async Task MissingFileGivesDefaultsWithInstanceId()
    {
        var options = await new SettingsSyncOptionsStore(FilePath, new ConfigurationProtection()).LoadAsync();

        Assert.False(options.Enabled);
        Assert.NotEqual(Guid.Empty, options.InstanceId);
        Assert.Equal(SettingsSyncOptions.DefaultCheckIntervalMinutes, options.CheckIntervalMinutes);
        Assert.Equal(SettingsSyncOptions.DefaultRetainedVersions, options.RetainedVersions);
    }

    [Fact]
    public async Task SavedOptionsAreEncryptedAndRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) return;
        var options = new SettingsSyncOptions
        {
            Enabled = true,
            DatabaseType = SettingsDatabaseType.MySql,
            Host = "db.example",
            Port = 3307,
            UserName = "nova",
            Password = "unique-db-password-marker",
            EncryptionPassword = "unique-encryption-password-marker",
            AutoUpload = true,
            CheckIntervalMinutes = 15,
            RetainedVersions = 7,
            LastKnownVersionId = 42
        };

        await new SettingsSyncOptionsStore(FilePath, new ConfigurationProtection()).SaveAsync(options);
        var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(FilePath));
        var restored = await new SettingsSyncOptionsStore(FilePath, new ConfigurationProtection()).LoadAsync();

        Assert.DoesNotContain("unique-db-password-marker", text);
        Assert.DoesNotContain("unique-encryption-password-marker", text);
        Assert.Equal(options.InstanceId, restored.InstanceId);
        Assert.Equal(SettingsDatabaseType.MySql, restored.DatabaseType);
        Assert.Equal("db.example", restored.Host);
        Assert.Equal(3307, restored.Port);
        Assert.Equal("unique-db-password-marker", restored.Password);
        Assert.Equal("unique-encryption-password-marker", restored.EncryptionPassword);
        Assert.True(restored.AutoUpload);
        Assert.Equal(15, restored.CheckIntervalMinutes);
        Assert.Equal(7, restored.RetainedVersions);
        Assert.Equal(42, restored.LastKnownVersionId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
