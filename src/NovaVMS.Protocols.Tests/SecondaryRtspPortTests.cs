using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class SecondaryRtspPortTests
{
    [Theory]
    [InlineData(DeviceProtocol.Isapi)]
    [InlineData(DeviceProtocol.Onvif)]
    public async Task SelectedSecondaryAddressUsesItsRtspPortWithoutChangingConfiguration(DeviceProtocol protocol)
    {
        var device = new Device { Host = "primary", SecondaryHost = "secondary", Protocol = protocol,
            RtspPort = 554, SecondaryPort = 18000, SecondaryRtspPort = 18554,
            Channels = [new Channel { Number = 1, MainStreamUri = "rtsp://internal:8554/video/main?channel=1" }] };
        var factory = new DeviceClientFactory(new Probe(false));
        using var client = await factory.CreateAsync(device);
        var uri = client.GetLiveSource(1, StreamType.Main).Uri!;
        Assert.Equal("secondary", uri.Host);
        Assert.Equal(18554, uri.Port);
        using var later = factory.Create(device);
        Assert.Equal(18554, later.GetLiveSource(1, StreamType.Main).Uri!.Port);
        if (protocol == DeviceProtocol.Isapi)
            Assert.Equal(18554, later.GetPlaybackSource(1, DateTime.Today, DateTime.Today.AddHours(1)).Uri!.Port);
        Assert.Equal(554, device.RtspPort);
        Assert.Null(device.RtspPortOverride);
        Assert.Equal("rtsp://internal:8554/video/main?channel=1", device.Channels[0].MainStreamUri);
    }

    [Theory]
    [InlineData(true, 18554, 8554)]
    [InlineData(false, null, 8554)]
    public async Task OnvifPreservesReportedPortForPrimaryOrUnconfiguredSecondary(bool primary, int? secondaryPort, int expected)
    {
        var device = new Device { Host = "primary", SecondaryHost = "secondary", Protocol = DeviceProtocol.Onvif,
            SecondaryRtspPort = secondaryPort, Channels = [new Channel { Number = 1, MainStreamUri = "rtsp://internal:8554/live" }] };
        var factory = new DeviceClientFactory(new Probe(primary));
        using var client = await factory.CreateAsync(device);
        Assert.Equal(expected, client.GetLiveSource(1, StreamType.Main).Uri!.Port);
    }

    [Fact]
    public async Task EditingSecondaryRtspPortInvalidatesPinnedEndpoint()
    {
        var device = new Device { Host = "primary", SecondaryHost = "secondary", SecondaryRtspPort = 18554 };
        var selector = new DeviceAddressSelector(new Probe(false));
        var signature = device.GetConnectionSignature();
        await selector.ResolveConnectionAsync(device, true, default);
        device.SecondaryRtspPort = 19554;
        Assert.Null(selector.GetSelectedHost(device));
        Assert.NotEqual(signature, device.GetConnectionSignature());
        Assert.Equal(19554, (await selector.ResolveConnectionAsync(device, true, default)).RtspPort);
    }

    private sealed class Probe(bool primary) : IDeviceReachability
    {
        public Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default)
            => Task.FromResult(device.Host == "secondary" || primary);
    }
}
