using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class PtzViewModelTests
{
    private readonly Device _device = new() { Name = "PTZ", Host = "192.168.1.20", Channels = [new Channel { Number = 1 }] };
    private readonly FakeDeviceClientFactory _factory = new();
    private readonly ConfigurationService _configuration;

    public PtzViewModelTests()
    {
        _configuration = new ConfigurationService(
            new MemoryConfigurationStore(new AppConfiguration { Devices = [_device] }), new JsonConfigurationSerializer());
    }

    [Fact]
    public async Task MoveAndStopShareOneClient()
    {
        using var ptz = await CreateAsync();

        await ptz.StartMoveAsync(PtzDirection.Up);
        await ptz.StopMoveAsync();

        var client = Assert.Single(_factory.Created);
        Assert.Equal(["move", "stop"], client.Commands);
        Assert.False(client.IsDisposed);
    }

    [Fact]
    public async Task StopReachesTheMovingCameraAfterTheTargetChanged()
    {
        using var ptz = await CreateAsync();

        await ptz.StartMoveAsync(PtzDirection.Left);
        ptz.SetTarget(null);
        await ptz.StopMoveAsync();

        Assert.Equal(["move", "stop"], Assert.Single(_factory.Created).Commands);
    }

    [Fact]
    public async Task ChangedConnectionSettingsReplaceTheClient()
    {
        using var ptz = await CreateAsync();
        await ptz.StartMoveAsync(PtzDirection.Up);
        await ptz.StopMoveAsync();

        _configuration.Current.Devices[0].Password = "changed";
        await ptz.StartMoveAsync(PtzDirection.Up);

        Assert.Equal(2, _factory.Created.Count);
        Assert.True(_factory.Created[0].IsDisposed);
        Assert.False(_factory.Created[1].IsDisposed);
    }

    [Fact]
    public async Task DisposeReleasesTheClient()
    {
        var ptz = await CreateAsync();
        await ptz.StartMoveAsync(PtzDirection.ZoomIn);

        ptz.Dispose();

        Assert.True(Assert.Single(_factory.Created).IsDisposed);
    }

    private async Task<PtzViewModel> CreateAsync()
    {
        await _configuration.LoadAsync();
        var ptz = new PtzViewModel(_configuration, _factory, new StatusBarService());
        ptz.SetTarget(new CameraReference { DeviceId = _device.Id, ChannelNumber = 1 });
        return ptz;
    }
}
