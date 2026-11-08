using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Mobile.Services;
using Xunit;

namespace NovaVMS.Mobile.Tests;

/// <summary>The phone and the desktop share the configuration format and file names.</summary>
public sealed class SharedConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-Mobile-Tests", Guid.NewGuid().ToString("N"));
    private readonly TestProtection _protection = new();

    [Fact]
    public async Task ExportFromThePhoneIsReadByTheDesktopSerializer()
    {
        var configuration = CreateConfiguration();

        await configuration.LoadAsync(TestContext.Current.CancellationToken);

        var device = new Device
        {
            Name = "Recorder",
            Host = "nvr.local",
            Password = "secret",
            Channels = [new Channel { Number = 1, Name = "Door", IsFavorite = true }]
        };

        configuration.Current.Devices.Add(device);
        configuration.Current.Views.Add(new SavedCameraView { Name = "Entrance" });
        configuration.Current.Settings.Theme = ThemeMode.Light;

        using var stream = new MemoryStream();

        await configuration.ExportAsync(stream, includePasswords: true);

        stream.Position = 0;

        var desktop = await new JsonConfigurationSerializer().DeserializeAsync(stream, TestContext.Current.CancellationToken);
        var exported = Assert.Single(desktop.Devices);

        Assert.Equal(device.Id, exported.Id);
        Assert.Equal("secret", exported.Password);
        Assert.True(Assert.Single(exported.Channels).IsFavorite);
        Assert.Equal("Entrance", Assert.Single(desktop.Views).Name);
        Assert.Equal(ThemeMode.Light, desktop.Settings.Theme);
    }

    [Fact]
    public async Task DesktopFileIsImportedOnThePhone()
    {
        var desktop = new AppConfiguration
        {
            Devices = [new Device { Name = "Office", Host = "office.local" }],
            Groups = [new CameraGroup { Name = "Office" }],
            Settings = new AppSettings { ShowTileTitles = false }
        };

        using var stream = new MemoryStream();

        await new JsonConfigurationSerializer().SerializeAsync(stream, desktop, TestContext.Current.CancellationToken);

        stream.Position = 0;

        var configuration = CreateConfiguration();

        await configuration.LoadAsync(TestContext.Current.CancellationToken);

        var imported = await configuration.ReadImportAsync(stream);
        var result = await configuration.ImportAsync(imported);

        Assert.Equal(1, result.DevicesAdded);
        Assert.Equal(1, result.GroupsAdded);
        Assert.False(configuration.Current.Settings.ShowTileTitles);
    }

    [Fact]
    public async Task FilesOfEarlierMobileVersionsGetTheSharedNames()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "config.enc"), "configuration", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "sync.enc"), "connection", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{\"UseOptionalProtocol\":true}", TestContext.Current.CancellationToken);

        var files = new MobileDataFiles(_directory);

        files.MigrateLegacyFiles();

        Assert.Equal(Path.Combine(_directory, AppDataPaths.ConfigurationFileName), files.ConfigurationFile);
        Assert.Equal("configuration", await File.ReadAllTextAsync(files.ConfigurationFile, TestContext.Current.CancellationToken));
        Assert.Equal("connection", await File.ReadAllTextAsync(files.SyncOptionsFile, TestContext.Current.CancellationToken));
        Assert.True(new LocalSettingsStore(files.LocalSettingsFile).Load().UseOptionalProtocol);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json")));
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
        var files = new MobileDataFiles(_directory);
        var store = new JsonConfigurationStore(new JsonConfigurationSerializer(), files.ConfigurationFile, new ConfigurationProtectionAdapter(_protection));

        return new MobileConfigurationService(store, new JsonConfigurationSerializer(), new MobileDeviceStore(_directory, _protection));
    }
}
