using System.Xml.Linq;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Isapi;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class VideoDisplayGeometryTests
{
    [Fact]
    public void KnownMainProportionsStayTheSameAcrossSubMainAndReconnect()
    {
        var geometry = new VideoDisplayGeometry();
        geometry.Reset("1920:1536");
        foreach (var ratio in new[] { "352:240", "1920:1536", "352:240", "1920:1536" })
        {
            geometry.Learn(ratio);
            Assert.Equal("5:4", geometry.AspectRatio);
        }
    }

    [Fact]
    public void FirstVisiblePictureIsPreservedWhenMainProportionsAreUnknown()
    {
        var geometry = new VideoDisplayGeometry();
        geometry.Learn("352:240");
        geometry.Learn("1920:1536");
        geometry.Learn("4:3");
        Assert.Equal("22:15", geometry.AspectRatio);
        geometry.Reset("1920:1080"); // Assigning a different camera starts a new geometry.
        Assert.Equal("16:9", geometry.AspectRatio);
    }

    [Theory]
    [InlineData(720u, 576u, 16u, 15u, "4:3")]
    [InlineData(1920u, 1536u, 1u, 1u, "5:4")]
    [InlineData(1920u, 1080u, 0u, 0u, "16:9")]
    [InlineData(0u, 576u, 1u, 1u, null)]
    public void DisplayProportionsIncludePixelAspectRatio(uint width, uint height, uint sarNum, uint sarDen, string? expected)
        => Assert.Equal(expected, VideoDisplayGeometry.FromDimensions(width, height, sarNum, sarDen));

    [Theory]
    [InlineData("1920", "1536", "5:4")]
    [InlineData("", "", null)]
    [InlineData("-1", "576", null)]
    public async Task ChannelSyncReadsMainEncoderGeometryBeforeFirstPlayback(string width, string height, string? expected)
    {
        using var transport = new ChannelTransport(width, height);
        var channels = await new IsapiDeviceService(transport).GetChannelsAsync(CancellationToken.None);
        var channel = Assert.Single(channels);
        Assert.Equal(1, channel.Number);
        Assert.Equal(expected, channel.MainAspectRatio);
    }

    private sealed class ChannelTransport(string width, string height) : IIsapiTransport
    {
        public Task<XDocument> GetXmlAsync(string path, CancellationToken cancellationToken) => Task.FromResult(
            path == "ISAPI/Streaming/channels" ? XDocument.Parse($"""
                <StreamingChannelList xmlns="http://www.hikvision.com/ver20/XMLSchema">
                  <StreamingChannel><id>101</id><Video>
                    <videoResolutionWidth>{width}</videoResolutionWidth>
                    <videoResolutionHeight>{height}</videoResolutionHeight>
                  </Video></StreamingChannel>
                  <StreamingChannel><id>102</id><Video>
                    <videoResolutionWidth>960</videoResolutionWidth>
                    <videoResolutionHeight>576</videoResolutionHeight>
                  </Video></StreamingChannel>
                </StreamingChannelList>
                """) : new XDocument(new XElement("InputProxyChannelStatusList")));
        public Task<XDocument> PostXmlAsync(string path, XElement body, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PutAsync(string path, XElement? body, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
