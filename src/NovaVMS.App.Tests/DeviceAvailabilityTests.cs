using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
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
        var device = new Device
        {
            Host = "first",
            SecondaryHost = "second",
            Protocol = DeviceProtocol.HikvisionSdk,
            SdkPort = 8000,
            SecondaryPort = 18000
        };
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
            var device = new Device
            {
                Host = "127.0.0.1",
                SecondaryHost = "127.0.0.1",
                Protocol = protocol,
                HttpPort = closed,
                SdkPort = closed,
                SecondaryPort = port
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            Assert.True(await DeviceAvailability.CheckAsync(new DeviceReachability(), device, timeout.Token));

            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
        }
        finally
        {
            listener.Stop();
        }
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
        var probe = new Probe((_, _) =>
        {
            calls++;

            return Task.FromResult(true);
        });

        Assert.True(await DeviceAvailability.CheckAsync(probe, device));
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
    public async Task SdkDeviceIsAvailableWhenOnlyRtspAnswers()
    {
        var device = new Device { Host = "nvr", Protocol = DeviceProtocol.HikvisionSdk, SdkPort = 8000, HttpPort = 80, RtspPort = 554 };
        var probe = new Probe((_, _) => Task.FromResult(false), (_, port) => port == 554);

        Assert.True(await DeviceAvailability.CheckAsync(probe, device));
        Assert.Contains(("nvr", 554), probe.Ports);
        Assert.DoesNotContain(probe.Ports, endpoint => endpoint.Port == 8000 || endpoint.Port == 80);
    }

    [Theory]
    [InlineData(DeviceProtocol.Isapi)]
    [InlineData(DeviceProtocol.Onvif)]
    public async Task HttpDeviceIsAvailableWhenOnlySdkOrRtspAnswers(DeviceProtocol protocol)
    {
        var device = new Device { Host = "camera", Protocol = protocol, HttpPort = 80, SdkPort = 8000, RtspPort = 554 };
        var sdk = new Probe((_, _) => Task.FromResult(false), (_, port) => port == 8000);
        var rtsp = new Probe((_, _) => Task.FromResult(false), (_, port) => port == 554);
        var none = new Probe((_, _) => Task.FromResult(false));

        Assert.True(await DeviceAvailability.CheckAsync(sdk, device));
        Assert.True(await DeviceAvailability.CheckAsync(rtsp, device));
        Assert.False(await DeviceAvailability.CheckAsync(none, device));
        Assert.Contains(("camera", 8000), none.Ports);
        Assert.Contains(("camera", 554), none.Ports);
    }

    [Fact]
    public async Task SecondaryAddressUsesItsOwnAlternativePorts()
    {
        var device = new Device
        {
            Host = "first",
            SecondaryHost = "second",
            Protocol = DeviceProtocol.Isapi,
            SdkPort = 8000,
            RtspPort = 554,
            SecondaryPort = 8080,
            SecondarySdkPort = 18000,
            SecondaryRtspPort = 10554
        };
        var probe = new Probe((_, _) => Task.FromResult(false));

        Assert.False(await DeviceAvailability.CheckAsync(probe, device));
        Assert.Contains(("second", 18000), probe.Ports);
        Assert.Contains(("second", 10554), probe.Ports);
        Assert.Contains(("first", 8000), probe.Ports);
        Assert.Contains(("first", 554), probe.Ports);
    }

    [Fact]
    public async Task DoesNotProbeAlternativePortEqualToControlPort()
    {
        var device = new Device { Host = "same", Protocol = DeviceProtocol.Isapi, HttpPort = 554, RtspPort = 554, SdkPort = 554 };
        var probe = new Probe((_, _) => Task.FromResult(false));

        Assert.False(await DeviceAvailability.CheckAsync(probe, device));
        Assert.Empty(probe.Ports);
    }

    [Fact]
    public async Task FailedSynchronizationDoesNotMarkDeviceOfflineBeforeAvailabilityCheck()
    {
        var device = new Device { Host = "recorder", Channels = [new Channel { Number = 1 }] };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device] }), new JsonConfigurationSerializer());
        var factory = new FakeDeviceClientFactory
        {
            ConfigureClient = client => client.ReadInfo = _ => throw new DeviceCommunicationException("unreachable")
        };
        var sync = new DeviceSyncService(configuration, factory);

        await configuration.LoadAsync();

        var result = await sync.TrySyncAsync(device);

        Assert.False(result.Success);
        Assert.False(sync.IsOffline(device.Id));
        Assert.False(sync.IsOnline(device.Id));

        await sync.RefreshAvailabilityAsync(new Probe((_, _) => Task.FromResult(false)), default);

        Assert.True(sync.IsOffline(device.Id));
    }

    [Fact]
    public async Task RecorderRowFollowsReachabilityAcrossRefreshAndTreeRebuild()
    {
        var device = new Device { Host = "primary", SecondaryHost = "backup", Channels = [new Channel { Number = 1 }] };
        var group = new CameraGroup
        {
            Name = "Recorder",
            SourceDeviceId = device.Id,
            Cameras = [new CameraReference { DeviceId = device.Id, ChannelNumber = 1 }]
        };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new()
        {
            Devices = [device],
            Groups = [group]
        }), new JsonConfigurationSerializer());
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

    private sealed class Probe(Func<Device, CancellationToken, Task<bool>> check, Func<string, int, bool>? portCheck = null) : IDeviceReachability
    {
        public ConcurrentBag<(string Host, int Port)> Ports { get; } = new();

        public Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default) => check(device, cancellationToken);

        public Task<bool> IsPortReachableAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            Ports.Add((host, port));

            return Task.FromResult(portCheck?.Invoke(host, port) ?? false);
        }
    }
}
