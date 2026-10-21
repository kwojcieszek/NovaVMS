using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class GroupLiveViewTests
{
    [Fact]
    public async Task IndependentGroupLayoutsAndEmptySlotsSurviveSaveAndReload()
    {
        var camera = new CameraReference { DeviceId = Guid.NewGuid(), ChannelNumber = 2 };
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        var configuration = new AppConfiguration();
        configuration.GroupLiveViews[first] = new() { LayoutId = "wide-9", LayoutCount = 9, Cameras = [null, camera, null], EmptySlots = [0] };
        configuration.GroupLiveViews[second] = new() { LayoutCount = 16, Cameras = [camera] };
        configuration.GroupLiveViews["special:Favorites"] = new() { LayoutCount = 4, Cameras = [camera] };
        var snapshot = configuration.CreateSnapshot();
        configuration.GroupLiveViews[first].LayoutCount = 1;
        configuration.GroupLiveViews[first].Cameras.Clear();
        configuration.GroupLiveViews[first].EmptySlots.Clear();
        var serializer = new JsonConfigurationSerializer();
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, snapshot);
        stream.Position = 0;
        var restored = await serializer.DeserializeAsync(stream);
        Assert.Equal(9, restored.GroupLiveViews[first].LayoutCount);
        Assert.Equal("wide-9", restored.GroupLiveViews[first].LayoutId);
        Assert.Equal(new CameraReference?[] { null, camera, null }, restored.GroupLiveViews[first].Cameras);
        Assert.Equal(new[] { 0 }, restored.GroupLiveViews[first].EmptySlots);
        Assert.Equal(16, restored.GroupLiveViews[second].LayoutCount);
        Assert.Equal(4, restored.GroupLiveViews["special:Favorites"].LayoutCount);
    }

    [Fact]
    public async Task OldConfigurationsStartWithoutSavedGroupViews()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));
        var restored = await new JsonConfigurationSerializer().DeserializeAsync(stream);
        Assert.Empty(restored.GroupLiveViews);
    }
}


