using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class GroupUnavailableCameraTests
{
    [Fact]
    public async Task GroupIsMarkedOnlyWhileOneOfItsCamerasIsKnownToBeUnavailable()
    {
        var online = new Device { Host = "online", Channels = [new Channel { Number = 1 }] };
        var offline = new Device { Host = "offline", Channels = [new Channel { Number = 1 }] };
        var mixed = new CameraGroup
        {
            Name = "Mixed",
            Cameras =
            [
                new CameraReference { DeviceId = online.Id, ChannelNumber = 1 },
                new CameraReference { DeviceId = offline.Id, ChannelNumber = 1 }
            ]
        };
        var healthy = new CameraGroup
        {
            Name = "Healthy",
            Cameras = [new CameraReference { DeviceId = online.Id, ChannelNumber = 1 }]
        };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new()
        {
            Devices = [online, offline],
            Groups = [mixed, healthy]
        }), new JsonConfigurationSerializer());
        var sync = new DeviceSyncService(configuration, new FakeDeviceClientFactory());
        var tree = new ResourceTreeViewModel(configuration, sync, null!, TestSessions.Create(configuration));

        await configuration.LoadAsync();

        var mixedNode = tree.Groups.Single(node => node.Id == mixed.Id);
        var healthyNode = tree.Groups.Single(node => node.Id == healthy.Id);

        // Availability is not known yet, so no group is marked.
        Assert.False(mixedNode.HasUnavailableCamera);
        Assert.False(healthyNode.HasUnavailableCamera);

        var offlineReachable = false;
        var probe = FakeReachability.Answering(device => device.Host == "online" || offlineReachable);

        await sync.RefreshAvailabilityAsync(probe, default);

        Assert.True(mixedNode.HasUnavailableCamera);
        Assert.False(healthyNode.HasUnavailableCamera);

        offlineReachable = true;

        await sync.RefreshAvailabilityAsync(probe, default);

        Assert.False(mixedNode.HasUnavailableCamera);
    }
}
