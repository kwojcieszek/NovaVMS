using System.Text;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class CameraIdentityTests
{
    [Fact]
    public async Task ReferencesByChannelNumberAreConvertedToIdentitiesOnLoad()
    {
        var device = new Device { Host = "nvr", Channels = [new Channel { Number = 1, Name = "Gate" }, new Channel { Number = 2, Name = "Yard" }] };
        var legacy = new CameraReference { DeviceId = device.Id, ChannelNumber = 2 };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new AppConfiguration
        {
            Devices = [device],
            Groups = [new CameraGroup { Name = "Outside", Cameras = [legacy] }],
            Views = [new SavedCameraView { Name = "View", Layout = new GroupLiveView { Cameras = [null, legacy] } }],
            GroupLiveViews = { ["key"] = new GroupLiveView { Cameras = [legacy] } }
        }), new JsonConfigurationSerializer());

        await configuration.LoadAsync();

        var yard = device.Channels[1].Id;
        Assert.Equal(yard, configuration.Current.Groups[0].Cameras[0].ChannelId);
        Assert.Equal(yard, configuration.Current.Views[0].Layout.Cameras[1]!.ChannelId);
        Assert.Equal(yard, configuration.Current.GroupLiveViews["key"].Cameras[0]!.ChannelId);
        Assert.Equal("Yard", configuration.FindCamera(configuration.Current.Views[0].Layout.Cameras[1]!)!.Channel.Name);

        using var stream = new MemoryStream();
        await new JsonConfigurationSerializer().SerializeAsync(stream, configuration.Current.CreateSnapshot());
        Assert.DoesNotContain("\"ChannelNumber\"", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void RenumberedChannelsKeepTheirIdentityByName()
    {
        var gate = new Channel { Number = 33, Name = "Gate", IsFavorite = true };
        var yard = new Channel { Number = 34, Name = "Yard" };
        var current = new List<Channel> { new() { Number = 1, Name = "Yard" }, new() { Number = 2, Name = "Gate" } };

        ChannelIdentity.Keep([gate, yard], current);

        Assert.Equal(yard.Id, current[0].Id);
        Assert.Equal(gate.Id, current[1].Id);
        Assert.True(current[1].IsFavorite);
    }

    [Fact]
    public void ChannelsWithoutNamesMatchByNumberAndKeepTheirNames()
    {
        var known = new Channel { Number = 2, Name = "Office" };
        var current = new List<Channel> { new() { Number = 1 }, new() { Number = 2 } };

        ChannelIdentity.Keep([known], current);

        Assert.NotEqual(known.Id, current[0].Id);
        Assert.Equal(known.Id, current[1].Id);
        Assert.Equal("Office", current[1].Name);
    }

    [Fact]
    public void ReorderedOnvifProfilesMatchByProfileAndRepeatedNamesDoNotDecide()
    {
        var first = new Channel { Number = 1, Name = "Camera", ProfileToken = "A" };
        var second = new Channel { Number = 2, Name = "Camera", ProfileToken = "B" };
        var current = new List<Channel>
        {
            new() { Number = 1, Name = "Camera", ProfileToken = "B" },
            new() { Number = 2, Name = "Camera", ProfileToken = "A" }
        };

        ChannelIdentity.Keep([first, second], current);

        Assert.Equal(second.Id, current[0].Id);
        Assert.Equal(first.Id, current[1].Id);
    }

    [Fact]
    public void DraggedCamerasCarryTheirIdentity()
    {
        var device = new Device();
        var channel = new Channel { Number = 7 };
        var reference = CameraReference.For(device, channel);

        var decoded = Assert.Single(CameraDragData.Decode(CameraDragData.Encode([reference])));

        Assert.Equal(reference, decoded);
        Assert.Equal(channel.Id, decoded.ChannelId);
    }

    [Fact]
    public void ReferencesAreEqualByIdentityNotByNumber()
    {
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        Assert.Equal(new CameraReference { DeviceId = deviceId, ChannelId = channelId },
            new CameraReference { DeviceId = deviceId, ChannelId = channelId, ChannelNumber = 5 });
        Assert.NotEqual(new CameraReference { DeviceId = deviceId, ChannelId = channelId },
            new CameraReference { DeviceId = deviceId, ChannelId = Guid.NewGuid() });
    }
}
