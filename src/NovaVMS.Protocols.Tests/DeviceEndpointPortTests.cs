using System.Net;
using System.Net.Sockets;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class DeviceEndpointPortTests
{
    [Theory]
    [InlineData(DeviceProtocol.Isapi)]
    [InlineData(DeviceProtocol.Onvif)]
    [InlineData(DeviceProtocol.HikvisionSdk)]
    public async Task SecondaryProbeAndPinnedClientsUsePairedPort(DeviceProtocol protocol)
    {
        var device = new Device { Host = "local", SecondaryHost = "remote", Protocol = protocol,
            HttpPort = 8080, SdkPort = 8000, SecondaryPort = 18000 };
        var probed = new List<(string, int)>();
        var factory = new DeviceClientFactory(new Probe(d =>
        {
            probed.Add((d.Host, d.ConnectionPort));
            return d.Host == "remote" && d.ConnectionPort == 18000;
        }));
        using var client = await factory.CreateAsync(device);
        Assert.Equal(new[] { ("local", device.ConnectionPort), ("remote", 18000) }, probed);
        var selected = factory.GetSelectedConnection(device);
        Assert.Equal("remote", selected.Host);
        Assert.Equal(18000, selected.ConnectionPort);
        using var later = factory.Create(device);
        Assert.Equal(2, probed.Count);
        Assert.Equal(protocol == DeviceProtocol.HikvisionSdk ? 8000 : 8080, device.ConnectionPort);
    }

    [Theory]
    [InlineData(DeviceProtocol.Isapi)]
    [InlineData(DeviceProtocol.HikvisionSdk)]
    public async Task SameHostWithDifferentPortsIsNotTreatedAsDuplicate(DeviceProtocol protocol)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var unavailable = new TcpListener(IPAddress.Loopback, 0);
        unavailable.Start();
        var primaryPort = ((IPEndPoint)unavailable.LocalEndpoint).Port;
        unavailable.Stop();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var device = new Device { Host = "127.0.0.1", SecondaryHost = "127.0.0.1", Protocol = protocol,
                HttpPort = primaryPort, SdkPort = primaryPort, SecondaryPort = port };
            var factory = new DeviceClientFactory();
            using var client = await factory.CreateAsync(device);
            Assert.Equal(port, factory.GetSelectedConnection(device).ConnectionPort);
            Assert.Equal(primaryPort, device.ConnectionPort);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task EditingOnlySecondaryPortInvalidatesChoiceAndConnectionSignature()
    {
        var device = new Device { Host = "a", SecondaryHost = "b", SecondaryPort = 8001 };
        var selector = new DeviceAddressSelector(new Probe(d => d.Host == "b"));
        var signature = device.GetConnectionSignature();
        Assert.Equal(8001, (await selector.ResolveConnectionAsync(device, true, default)).ConnectionPort);
        device.SecondaryPort = 8002;
        Assert.Null(selector.GetSelectedHost(device));
        Assert.NotEqual(signature, device.GetConnectionSignature());
        Assert.Equal(8002, (await selector.ResolveConnectionAsync(device, true, default)).ConnectionPort);
    }

    [Fact]
    public async Task LegacySecondaryAddressUsesPrimaryProtocolPort()
    {
        var device = new Device { Host = "a", SecondaryHost = "b", SdkPort = 8123, Protocol = DeviceProtocol.HikvisionSdk };
        var selector = new DeviceAddressSelector(new Probe(d => d.Host == "b"));
        Assert.Equal(8123, (await selector.ResolveConnectionAsync(device, true, default)).ConnectionPort);
    }

    private sealed class Probe(Func<Device, bool> check) : IDeviceReachability
    {
        public Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default) => Task.FromResult(check(device));
    }
}
