using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Import;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class ImportedCameraGroupsTests
{
    [Fact]
    public void GroupsMapToExistingDevicesAndRepeatedImportsDoNotDuplicateMembers()
    {
        var existing = new Device { Host = "NVR", Protocol = DeviceProtocol.HikvisionSdk };
        var source = new Device { Host = " nvr ", Protocol = DeviceProtocol.HikvisionSdk,
            Channels = [new Channel { Number = 3, Name = "Entrance" }] };
        var importedGroup = new CameraGroup { Name = "Office", Cameras =
            [new CameraReference { DeviceId = source.Id, ChannelNumber = 3 }] };
        var manual = new CameraReference { DeviceId = existing.Id, ChannelNumber = 99 };
        var existingGroup = new CameraGroup { Name = "office", Cameras = [manual] };
        var configuration = new AppConfiguration { Devices = [existing], Groups = [existingGroup] };
        var import = new DeviceCsvResult([source], []) { Groups = [importedGroup] };
        ImportedCameraGroups.Merge(configuration, import);
        ImportedCameraGroups.Merge(configuration, import);
        Assert.Single(configuration.Devices);
        Assert.Same(existingGroup, Assert.Single(configuration.Groups));
        Assert.Equal(2, existingGroup.Cameras.Count);
        Assert.Equal(manual, existingGroup.Cameras[0]);
        Assert.Equal(existing.Id, existingGroup.Cameras[1].DeviceId);
        Assert.Equal(3, existingGroup.Cameras[1].ChannelNumber);
        Assert.Equal("Entrance", Assert.Single(existing.Channels).Name);
    }

    [Fact]
    public void EmptyGroupsAndDifferentPortsRemainSeparate()
    {
        var first = new Device { Host = "nvr", SdkPort = 8000, Protocol = DeviceProtocol.HikvisionSdk,
            Channels = [new Channel { Number = 1 }] };
        var second = new Device { Host = "nvr", SdkPort = 9000, Protocol = DeviceProtocol.HikvisionSdk,
            Channels = [new Channel { Number = 1 }] };
        var configuration = new AppConfiguration { Devices = [first, second] };
        var import = new DeviceCsvResult([first, second], []) { Groups = [new() { Name = "Empty" },
            new() { Name = "Mixed", Cameras = [new() { DeviceId = first.Id, ChannelNumber = 1 },
                new() { DeviceId = second.Id, ChannelNumber = 1 }] }] };
        ImportedCameraGroups.Merge(configuration, import);
        Assert.Equal(2, configuration.Groups.Count);
        Assert.Empty(configuration.Groups[0].Cameras);
        Assert.Equal(new[] { first.Id, second.Id }, configuration.Groups[1].Cameras.Select(camera => camera.DeviceId));
    }
}
