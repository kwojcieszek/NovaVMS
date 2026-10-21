using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class DeviceAddressSelectorTests
{
    [Theory]
    [InlineData(true, true, "primary", 1)]
    [InlineData(false, true, "secondary", 2)]
    [InlineData(false, false, "primary", 2)]
    public async Task ChoosesInOrderAndNeverRechecksEvenWhenConnectivityChanges(bool primary, bool secondary, string expected, int calls)
    {
        var device = Device();
        var probe = new Probe(d => Task.FromResult(d.Host == "primary" ? primary : secondary));
        var selector = new DeviceAddressSelector(probe);
        Assert.Null(selector.GetSelectedHost(device));
        Assert.Equal(expected, await selector.ResolveAsync(device, true, default));
        probe.Check = _ => throw new InvalidOperationException("A selected address must not be checked again.");
        for (var i = 0; i < 3; i++)
            Assert.Equal(expected, await selector.ResolveAsync(device, true, default));
        Assert.Equal(expected, selector.GetSelectedHost(device));
        Assert.Equal(calls, probe.Hosts.Count);
        Assert.Equal("primary", device.Host);
        Assert.Equal("secondary", device.SecondaryHost);
    }

    [Theory]
    [InlineData("")]
    [InlineData("primary")]
    [InlineData(" PRIMARY ")]
    public async Task MissingOrDuplicateSecondaryDoesNotAddAProbe(string secondary)
    {
        var device = Device();
        device.SecondaryHost = secondary;
        var probe = new Probe(_ => throw new InvalidOperationException());
        var selector = new DeviceAddressSelector(probe);
        Assert.Equal("primary", await selector.ResolveAsync(device, true, default));
        Assert.Empty(probe.Hosts);
    }

    [Fact]
    public async Task ConcurrentCameraOpeningsShareOneChoiceAndCancellationDoesNotPoisonIt()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Probe(async d => { await gate.Task; return d.Host == "secondary"; });
        var selector = new DeviceAddressSelector(probe);
        var device = Device();
        using var stop = new CancellationTokenSource();
        var canceled = selector.ResolveAsync(device, true, stop.Token);
        var others = Enumerable.Range(0, 8).Select(_ => selector.ResolveAsync(device, true, default)).ToArray();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        gate.SetResult();
        Assert.All(await Task.WhenAll(others), host => Assert.Equal("secondary", host));
        Assert.Equal(new[] { "primary", "secondary" }, probe.Hosts);
    }

    [Fact]
    public async Task SetupDoesNotPinAnAddressButUsesAnExistingChoice()
    {
        var probe = new Probe(d => Task.FromResult(d.Host == "secondary"));
        var selector = new DeviceAddressSelector(probe);
        var device = Device();
        Assert.Equal("secondary", await selector.ResolveAsync(device, false, default));
        Assert.Null(selector.GetSelectedHost(device));
        probe.Check = _ => Task.FromResult(true);
        Assert.Equal("primary", await selector.ResolveAsync(device, true, default));
        probe.Check = _ => throw new InvalidOperationException();
        Assert.Equal("primary", await selector.ResolveAsync(device, false, default));
        Assert.Equal(3, probe.Hosts.Count);
    }

    [Fact]
    public async Task EditingAddressesOrRestartingTheAppAllowsANewChoice()
    {
        var probe = new Probe(d => Task.FromResult(d.Host != "primary"));
        var device = Device();
        var selector = new DeviceAddressSelector(probe);
        Assert.Equal("secondary", await selector.ResolveAsync(device, true, default));
        device.Host = "new-primary";
        Assert.Null(selector.GetSelectedHost(device));
        Assert.Equal("new-primary", await selector.ResolveAsync(device, true, default));
        Assert.Null(new DeviceAddressSelector(probe).GetSelectedHost(device));
    }

    [Theory]
    [InlineData(DeviceProtocol.Isapi)]
    [InlineData(DeviceProtocol.Onvif)]
    public async Task LivePlaybackAndLaterClientsUseTheChosenHostWithoutChangingSavedUris(DeviceProtocol protocol)
    {
        var device = Device();
        device.Protocol = protocol;
        device.Channels = [new Channel { Number = 1, MainStreamUri = "rtsp://primary:8554/video/main?channel=1" }];
        var factory = new DeviceClientFactory(new Probe(d => Task.FromResult(d.Host == "secondary")));
        using var client = await factory.CreateAsync(device);
        Assert.Equal("secondary", client.GetLiveSource(1, StreamType.Main).Uri!.Host);
        using var later = factory.Create(device);
        Assert.Equal("secondary", later.GetLiveSource(1, StreamType.Main).Uri!.Host);
        if (protocol == DeviceProtocol.Isapi)
            Assert.Equal("secondary", later.GetPlaybackSource(1, DateTime.Today, DateTime.Today.AddHours(1)).Uri!.Host);
        Assert.Equal("primary", device.Host);
        Assert.Equal("rtsp://primary:8554/video/main?channel=1", device.Channels[0].MainStreamUri);
    }

    private static Device Device() => new() { Host = "primary", SecondaryHost = "secondary" };

    [Fact]
    public async Task RetainOnlyForgetsRemovedDevicesAndPreviousSettings()
    {
        var probe = new Probe(d => Task.FromResult(d.Host == "secondary"));
        var selector = new DeviceAddressSelector(probe);
        var kept = Device();
        var edited = Device();
        var removed = Device();
        foreach (var device in new[] { kept, edited, removed })
            await selector.ResolveAsync(device, true, default);

        edited.HttpPort = 8080;
        selector.RetainOnly([kept, edited]);

        Assert.Equal("secondary", selector.GetSelectedHost(kept));
        Assert.Null(selector.GetSelectedHost(edited));
        Assert.Null(selector.GetSelectedHost(removed));
    }

    [Theory]
    [InlineData(DeviceProtocol.Isapi)]
    [InlineData(DeviceProtocol.Onvif)]
    [InlineData(DeviceProtocol.HikvisionSdk)]
    public async Task RealTcpProbeUsesTheConfiguredProtocolPortAndSelectsTheReachableHost(DeviceProtocol protocol)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var device = new Device { Host = "127.0.0.2", SecondaryHost = "127.0.0.1", Protocol = protocol,
                HttpPort = protocol == DeviceProtocol.HikvisionSdk ? 1 : port,
                SdkPort = protocol == DeviceProtocol.HikvisionSdk ? port : 1 };
            var factory = new DeviceClientFactory();
            using var client = await factory.CreateAsync(device);
            Assert.Equal("127.0.0.1", factory.GetSelectedHost(device));
            Assert.Equal("127.0.0.2", device.Host);
        }
        finally { listener.Stop(); }
    }

    private sealed class Probe(Func<Device, Task<bool>> check) : IDeviceReachability
    {
        public Func<Device, Task<bool>> Check = check;
        public ConcurrentQueue<string> Hosts { get; } = new();
        public Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default)
        {
            Hosts.Enqueue(device.Host);
            return Check(device);
        }
    }
}
