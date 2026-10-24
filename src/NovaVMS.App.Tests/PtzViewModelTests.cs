using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Core.Abstractions;
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

    [Fact]
    public async Task PtzResolvesReachableEndpointBeforeCreatingAndReusingClient()
    {
        await _configuration.LoadAsync();
        var device = _configuration.Current.Devices[0];
        device.SecondaryHost = "backup-recorder";
        device.SecondaryPort = 9000;
        var factory = new ResolvingFactory(_factory);
        using var ptz = new PtzViewModel(_configuration, factory, new StatusBarService());
        ptz.SetTarget(CameraReference.For(device, device.Channels[0]));
        await ptz.StartMoveAsync(PtzDirection.Up);
        await ptz.StopMoveAsync();
        var client = Assert.Single(_factory.Created);
        Assert.Equal("backup-recorder", client.Device.Host);
        Assert.Equal(9000, client.Device.ConnectionPort);
        Assert.Equal(["move", "stop"], client.Commands);
    }

    private sealed class ResolvingFactory(FakeDeviceClientFactory inner) : IDeviceClientFactory
    {
        public IDeviceClient Create(Device device) => throw new InvalidOperationException("Resolve the endpoint asynchronously first.");
        public async Task<IDeviceClient> CreateAsync(Device device, bool rememberAddress = true, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return inner.Create(device.ForSecondaryAddress());
        }
    }

    [Fact]
    public async Task ShutdownWaitsForActiveCommandBeforeReleasingItsClient()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.ConfigureClient = client => client.Move = () => release.Task;
        var ptz = await CreateAsync();
        var moving = ptz.StartMoveAsync(PtzDirection.Up);
        try
        {
            ptz.Dispose();
            var client = Assert.Single(_factory.Created);
            Assert.False(client.IsDisposed);
            Assert.False(ptz.Disposal.IsCompleted);
            var queued = ptz.StartMoveAsync(PtzDirection.Left);
            release.SetResult();
            await Task.WhenAll(moving, queued, ptz.Disposal).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(client.IsDisposed);
            Assert.Equal(["move"], client.Commands);
        }
        finally { release.TrySetResult(); ptz.Dispose(); await ptz.Disposal; }
    }
}
