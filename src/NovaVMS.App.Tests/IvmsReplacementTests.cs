using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class IvmsReplacementTests
{
    [Fact]
    public async Task ReplacementRemovesOldResourcesAndLayoutsAndPreservesGeneralSettings()
    {
        var store = new MemoryStore();
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        var old = new Device { Host = "old" };
        configuration.Current.Devices.Add(old);
        configuration.Current.Groups.Add(new CameraGroup { Name = "Old group" });
        configuration.Current.GroupLiveViews["old"] = new GroupLiveView { LayoutCount = 9 };
        configuration.Current.Settings.Theme = ThemeMode.Light;
        configuration.Current.Settings.HcNetSdkDirectory = "local-sdk";
        configuration.Current.Settings.NetworkCachingMs = 123;
        var device = new Device { Host = "new", Channels = [new Channel { Number = 1 }] };
        var group = new CameraGroup { Name = "Imported", Cameras = [new CameraReference { DeviceId = device.Id, ChannelNumber = 1 }] };
        var replaced = 0;
        var changed = 0;
        var saved = 0;
        configuration.Replaced += (_, _) => replaced++;
        configuration.Changed += (_, _) => changed++;
        configuration.Saved += (_, _) => saved++;
        await configuration.ReplaceDevicesAndGroupsAsync([device], [group]);
        Assert.Equal(device.Id, Assert.Single(configuration.Current.Devices).Id);
        Assert.Equal(group.Id, Assert.Single(configuration.Current.Groups).Id);
        Assert.Equal(device.Id, configuration.Current.Groups[0].Cameras[0].DeviceId);
        Assert.Empty(configuration.Current.GroupLiveViews);
        Assert.Equal(ThemeMode.Light, configuration.Current.Settings.Theme);
        Assert.Equal("local-sdk", configuration.Current.Settings.HcNetSdkDirectory);
        Assert.Equal(123, configuration.Current.Settings.NetworkCachingMs);
        Assert.Equal((1, 1, 1), (replaced, changed, saved));
        Assert.Equal(device.Id, Assert.Single(store.Saved!.Devices).Id);
        Assert.Empty(store.Saved.GroupLiveViews);
    }

    [Fact]
    public async Task FailedWriteKeepsExistingConfigurationAndDoesNotSignalReplacement()
    {
        var store = new MemoryStore { Fail = true };
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        var original = configuration.Current;
        var device = new Device { Host = "old" };
        original.Devices.Add(device);
        original.Groups.Add(new CameraGroup { Name = "Keep" });
        var replaced = false;
        configuration.Replaced += (_, _) => replaced = true;
        await Assert.ThrowsAsync<IOException>(() => configuration.ReplaceDevicesAndGroupsAsync([new Device()], []));
        Assert.Same(original, configuration.Current);
        Assert.Same(device, Assert.Single(configuration.Current.Devices));
        Assert.Equal("Keep", Assert.Single(configuration.Current.Groups).Name);
        Assert.False(replaced);
    }

    [Fact]
    public async Task ImportWithoutGroupsClearsPreviousGroups()
    {
        var configuration = new ConfigurationService(new MemoryStore(), new JsonConfigurationSerializer());
        configuration.Current.Groups.Add(new CameraGroup());
        await configuration.ReplaceDevicesAndGroupsAsync([new Device()], []);
        Assert.Empty(configuration.Current.Groups);
    }

    private sealed class MemoryStore : IConfigurationStore
    {
        public bool Fail { get; init; }
        public AppConfiguration? Saved { get; private set; }
        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppConfiguration());
        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Test write failure");
            Saved = configuration;
            return Task.CompletedTask;
        }
    }
}
