using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class DeviceSyncShutdownTests
{
    [Fact]
    public async Task ShutdownCancelsAndDrainsNativeCallsBeforeSdkCanBeReleased()
    {
        var device = new Device { Host = "test-device" };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new AppConfiguration()), new JsonConfigurationSerializer());
        var release = new TaskCompletionSource<DeviceInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeDeviceClientFactory
        {
            ConfigureClient = client => client.ReadInfo = token =>
            {
                entered.SetResult(token);
                // SDK calls may ignore cancellation until the native call returns.
                return release.Task;
            }
        };
        var sync = new DeviceSyncService(configuration, factory);
        var operation = sync.TrySyncAsync(device);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var shutdown = sync.ShutdownAsync();
            Assert.True(token.IsCancellationRequested);
            Assert.False(shutdown.IsCompleted);
            Assert.False(Assert.Single(factory.Created).IsDisposed);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync.TrySyncAsync(device));
            Assert.Single(factory.Created);
        }
        finally
        {
            release.TrySetCanceled(token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            await sync.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.True(Assert.Single(factory.Created).IsDisposed);
    }

    [Fact]
    public async Task FailedOperationDoesNotHoldShutdownOpen()
    {
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new AppConfiguration()), new JsonConfigurationSerializer());
        var sync = new DeviceSyncService(configuration, new FakeDeviceClientFactory());
        Assert.False((await sync.TrySyncAsync(new Device())).Success);
        await sync.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await sync.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(64)]
    public async Task RepeatedShutdownWithManyDevicesDrainsEveryClient(int deviceCount)
    {
        for (var cycle = 0; cycle < 25; cycle++)
        {
            var configuration = new ConfigurationService(new MemoryConfigurationStore(new AppConfiguration()), new JsonConfigurationSerializer());
            var factory = new FakeDeviceClientFactory
            {
                ConfigureClient = client => client.ReadInfo = async token =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return new DeviceInfo();
                }
            };
            var sync = new DeviceSyncService(configuration, factory);
            var operations = Enumerable.Range(0, deviceCount).Select(_ => sync.TrySyncAsync(new Device())).ToArray();
            await sync.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var operation in operations)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.Equal(deviceCount, factory.Created.Count);
            Assert.All(factory.Created, client => Assert.True(client.IsDisposed));
        }
    }
}
