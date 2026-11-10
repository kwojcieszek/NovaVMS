using Avalonia.Headless.XUnit;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Mobile.Services;
using NovaVMS.Presentation.Services;
using Xunit;

namespace NovaVMS.Mobile.Tests;

public sealed class MobileConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-Mobile-Tests", Guid.NewGuid().ToString("N"));
    private readonly TestProtection _protection = new();

    [Fact]
    public async Task LegacyDevicesAreMigratedIntoSharedConfigurationWithDeviceGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var legacy = new MobileDeviceStore(_directory, _protection);
        var device = new Device
        {
            Name = "Recorder",
            Host = "camera.local",
            Channels = [new Channel { Number = 1, Name = "Door", IsFavorite = true }]
        };

        await legacy.SaveAsync([device], cancellationToken);

        var configuration = CreateConfiguration();

        await configuration.LoadAsync(cancellationToken);

        var migrated = Assert.Single(configuration.Current.Devices);
        var group = Assert.Single(configuration.Current.Groups);

        Assert.Equal(device.Id, migrated.Id);
        Assert.True(migrated.Channels[0].IsFavorite);
        Assert.Equal(device.Id, group.SourceDeviceId);
        Assert.Single(group.Cameras);
        Assert.Empty(configuration.Current.Users);
        Assert.False(legacy.Exists);

        var reloaded = CreateConfiguration();

        await reloaded.LoadAsync(cancellationToken);

        Assert.Equal(device.Id, Assert.Single(reloaded.Current.Devices).Id);
    }

    [AvaloniaFact]
    public async Task NewerVersionFromAnotherInstallationReplacesConfiguration()
    {
        var configuration = CreateConfiguration();

        await configuration.LoadAsync(TestContext.Current.CancellationToken);

        var remote = new FakeRemoteSettingsStore();
        var options = new SettingsSyncOptions
        {
            Enabled = true,
            Host = "db.local",
            UserName = "novavms",
            EncryptionPassword = "secret",
            AutoCheck = false
        };
        var received = new AppConfiguration
        {
            Devices = [new Device { Name = "From desktop", Host = "nvr.local" }],
            Views = [new SavedCameraView { Name = "Entrance" }]
        };

        remote.Add(received, Guid.NewGuid());

        using var sync = new SettingsSyncService(configuration, new MemorySyncOptionsStore(options), remote);
        var replaced = false;

        configuration.Replaced += (_, _) => replaced = true;

        await sync.StartAsync();

        Assert.True(await sync.CheckForUpdatesAsync());
        Assert.True(replaced);
        Assert.Equal("From desktop", Assert.Single(configuration.Current.Devices).Name);
        Assert.Equal("Entrance", Assert.Single(configuration.Current.Views).Name);
        Assert.Equal(1, sync.LastKnownVersionId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private MobileConfigurationService CreateConfiguration()
    {
        var store = new JsonConfigurationStore(new JsonConfigurationSerializer(), Path.Combine(_directory, "config.enc"), new ConfigurationProtectionAdapter(_protection));

        return new MobileConfigurationService(store, new JsonConfigurationSerializer(), new MobileDeviceStore(_directory, _protection));
    }
}
