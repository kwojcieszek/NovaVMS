using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.HcNetSdk;
using NovaVMS.Protocols.Isapi;
using NovaVMS.Protocols.Onvif;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class PlaybackProtocolRoutingTests
{
    [Theory]
    [InlineData(DeviceProtocol.HikvisionSdk, typeof(HcNetSdkDeviceClient))]
    [InlineData(DeviceProtocol.Isapi, typeof(IsapiDeviceClient))]
    [InlineData(DeviceProtocol.Onvif, typeof(OnvifDeviceClient))]
    public void UsesSelectedRecorderProtocol(DeviceProtocol protocol, Type expected)
    {
        var factory = new DeviceClientFactory();
        using var client = factory.Create(new Device { Host = "recorder", Protocol = protocol });
        Assert.IsType(expected, client);
    }

    [Fact]
    public async Task IsapiPlaybackKeepsItsRtspPathWithoutLoadingSdk()
    {
        using IDeviceClient client = new IsapiDeviceClient(new Device { Host = "recorder", RtspPort = 8554 });
        var start = new DateTime(2026, 10, 20, 8, 30, 0);
        var source = await client.GetPlaybackSourceAsync(3, start, start.AddHours(1));
        Assert.False(source.IsStream);
        Assert.Equal("recorder", source.Uri!.Host);
        Assert.Equal(8554, source.Uri.Port);
        Assert.Equal("/Streaming/tracks/301", source.Uri.AbsolutePath);
        Assert.Contains("starttime=", source.Uri.Query);
    }
}
