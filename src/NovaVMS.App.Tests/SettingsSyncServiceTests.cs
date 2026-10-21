using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class SettingsSyncServiceTests
{
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly FakeRemoteSettingsStore _remote = new();
    private readonly MemoryConfigurationStore _localStore;
    private readonly MemorySyncOptionsStore _optionsStore;
    private readonly ConfigurationService _configuration;

    public SettingsSyncServiceTests()
    {
        _localStore = new MemoryConfigurationStore(Configuration("local camera", "C:\\sdk"));
        _optionsStore = new MemorySyncOptionsStore(new SettingsSyncOptions
        {
            Enabled = true,
            Host = "db",
            UserName = "user",
            EncryptionPassword = "password",
            InstanceId = _instanceId
        });
        _configuration = new ConfigurationService(_localStore, new JsonConfigurationSerializer());
    }

    [Fact]
    public async Task StartupAppliesNewerVersionFromAnotherComputerAndKeepsLocalSdkFolder()
    {
        var remote = _remote.Add(Configuration("remote camera", "D:\\other"), Guid.NewGuid());
        using var sync = await StartAsync();

        Assert.Equal("remote camera", Assert.Single(_configuration.Current.Devices).Name);
        Assert.Equal("C:\\sdk", _configuration.Current.Settings.HcNetSdkDirectory);
        Assert.Equal("remote camera", Assert.Single(_localStore.Stored.Devices).Name);
        Assert.Equal(remote.Id, _optionsStore.Options.LastKnownVersionId);
    }

    [Fact]
    public async Task OwnOrKnownVersionsAreNotApplied()
    {
        _remote.Add(Configuration("own camera", string.Empty), _instanceId);
        using var sync = await StartAsync();

        Assert.Equal("local camera", Assert.Single(_configuration.Current.Devices).Name);
        Assert.Equal(1, _optionsStore.Options.LastKnownVersionId);

        // A version already known here stays ignored even though it came from another computer.
        _optionsStore.Options.LastKnownVersionId = 0;
        await sync.MarkKnownAsync(_remote.Add(Configuration("other camera", string.Empty), Guid.NewGuid()).Id);
        await sync.CheckForUpdatesAsync();
        Assert.Equal("local camera", Assert.Single(_configuration.Current.Devices).Name);
    }

    [Fact]
    public async Task ArchivedVersionIsAppliedAndResentWhenAutoUploadIsOn()
    {
        _optionsStore.Options.AutoUpload = true;
        var archived = _remote.Add(Configuration("archived camera", string.Empty), Guid.NewGuid());
        var latest = _remote.Add(Configuration("latest camera", string.Empty), Guid.NewGuid());
        using var sync = await StartAsync();
        Assert.Equal("latest camera", Assert.Single(_configuration.Current.Devices).Name);

        Assert.True(await sync.ApplyVersionAsync(archived));

        Assert.Equal("archived camera", Assert.Single(_configuration.Current.Devices).Name);
        Assert.Equal(3, _remote.Count);
        var resent = (await _remote.GetLatestAsync())!;
        Assert.True(resent.Id > latest.Id);
        Assert.Equal(_instanceId, resent.InstanceId);
        Assert.Equal(resent.Id, _optionsStore.Options.LastKnownVersionId);
    }

    [Fact]
    public async Task ManualUploadStoresCurrentConfiguration()
    {
        using var sync = await StartAsync();

        Assert.True(await sync.UploadAsync());

        var latest = (await _remote.GetLatestAsync())!;
        Assert.Equal("local camera", Assert.Single((await _remote.DownloadAsync(latest.Id)).Devices).Name);
        Assert.Equal(latest.Id, _optionsStore.Options.LastKnownVersionId);
    }

    [Fact]
    public async Task DisabledSynchronizationDoesNothing()
    {
        _optionsStore.Options.Enabled = false;
        _remote.Add(Configuration("remote camera", string.Empty), Guid.NewGuid());
        using var sync = await StartAsync();

        Assert.False(await sync.UploadAsync());
        Assert.Equal("local camera", Assert.Single(_configuration.Current.Devices).Name);
        Assert.Equal(1, _remote.Count);
    }

    private async Task<SettingsSyncService> StartAsync()
    {
        await _configuration.LoadAsync();
        var sync = new SettingsSyncService(_configuration, _optionsStore, _remote, new StatusBarService());
        await sync.StartAsync();
        return sync;
    }

    private static AppConfiguration Configuration(string deviceName, string sdkDirectory) => new()
    {
        Devices = [new Device { Name = deviceName, Host = "192.168.1.10" }],
        Settings = new AppSettings { HcNetSdkDirectory = sdkDirectory }
    };
}
