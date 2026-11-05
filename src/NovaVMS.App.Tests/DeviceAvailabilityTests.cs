using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class DeviceAvailabilityTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task ChecksBothConfiguredPairsOnEveryRefresh(bool primary, bool secondary, bool expected)
    {
        var device = new Device { Host = "first", SecondaryHost = "second", Protocol = DeviceProtocol.HikvisionSdk,
            SdkPort = 8000, SecondaryPort = 18000 };
        var seen = new ConcurrentBag<(string, int)>();
        var probe = new Probe((endpoint, _) =>
        {
            seen.Add((endpoint.Host, endpoint.ConnectionPort));
            return Task.FromResult(endpoint.Host == "first" ? primary : secondary);
        });
        Assert.Equal(expected, await DeviceAvailability.CheckAsync(probe, device));
        Assert.Contains(("first", 8000), seen);
        Assert.Contains(("second", 18000), seen);
        seen.Clear();
        primary = false;
        secondary = true;
        Assert.True(await DeviceAvailability.CheckAsync(probe, device));
        Assert.Equal(2, seen.Count);
        Assert.Equal("first", device.Host);
        Assert.Equal(8000, device.SdkPort);
    }

    [Theory]
    [InlineData(DeviceProtocol.HikvisionSdk)]
    [InlineData(DeviceProtocol.Isapi)]
    [InlineData(DeviceProtocol.Onvif)]
    public async Task RealTcpConnectionsReachSecondaryPortOnSameHost(DeviceProtocol protocol)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        var unavailable = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        unavailable.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var closed = ((IPEndPoint)unavailable.LocalEndpoint).Port;
        unavailable.Stop();
        try
        {
            var device = new Device { Host = "127.0.0.1", SecondaryHost = "127.0.0.1", Protocol = protocol,
                HttpPort = closed, SdkPort = closed, SecondaryPort = port };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.True(await DeviceAvailability.CheckAsync(new DeviceReachability(), device, timeout.Token));
            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task UnreachablePrimaryDoesNotHideWorkingSecondary()
    {
        var device = new Device { Host = "a", SecondaryHost = "b" };
        Assert.True(await DeviceAvailability.CheckAsync(new Probe((endpoint, _) => endpoint.Host == "a"
            ? Task.FromException<bool>(new SocketException()) : Task.FromResult(true)), device));
    }

    [Fact]
    public async Task DoesNotProbeSamePairTwice()
    {
        var calls = 0;
        var device = new Device { Host = "same", SecondaryHost = " SAME ", HttpPort = 80, SecondaryPort = 80 };
        Assert.True(await DeviceAvailability.CheckAsync(new Probe((_, _) => { calls++; return Task.FromResult(true); }), device));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BothConnectionAttemptsStartWithoutWaitingForTheOther()
    {
        var entered = new ConcurrentBag<string>();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = DeviceAvailability.CheckAsync(new Probe((endpoint, _) =>
        {
            entered.Add(endpoint.Host);
            return release.Task;
        }), new Device { Host = "first", SecondaryHost = "second" });
        Assert.Equal(2, entered.Count);
        Assert.False(check.IsCompleted);
        release.SetResult(true);
        Assert.True(await check);
    }

    [Fact]
    public async Task RecorderRowFollowsReachabilityAcrossRefreshAndTreeRebuild()
    {
        var device = new Device { Host = "primary", SecondaryHost = "backup", Channels = [new Channel { Number = 1 }] };
        var group = new CameraGroup { Name = "Recorder", SourceDeviceId = device.Id,
            Cameras = [new CameraReference { DeviceId = device.Id, ChannelNumber = 1 }] };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new()
            { Devices = [device], Groups = [group] }), new JsonConfigurationSerializer());
        var sync = new DeviceSyncService(configuration, new FakeDeviceClientFactory());
        var tree = new ResourceTreeViewModel(configuration, sync, null!, TestSessions.Create(configuration));
        await configuration.LoadAsync();
        var recorder = tree.Groups.Single(node => node.Id == group.Id);
        Assert.False(recorder.IsRecorderOffline);

        bool backupOnline = false;
        var probe = new Probe((endpoint, _) => Task.FromResult(backupOnline && endpoint.Host == "backup"));
        await sync.RefreshAvailabilityAsync(probe, default);
        Assert.True(recorder.IsRecorderOffline);
        await configuration.SaveAsync();
        Assert.True(tree.Groups.Single(node => node.Id == group.Id).IsRecorderOffline);
        Assert.All(tree.Groups.Where(node => !node.IsDeviceGroup), node => Assert.False(node.IsRecorderOffline));

        backupOnline = true;
        await sync.RefreshAvailabilityAsync(probe, default);
        Assert.False(recorder.IsRecorderOffline);
    }

    private sealed class Probe(Func<Device, CancellationToken, Task<bool>> check) : IDeviceReachability
    {
        public Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default) => check(device, cancellationToken);
    }
}
