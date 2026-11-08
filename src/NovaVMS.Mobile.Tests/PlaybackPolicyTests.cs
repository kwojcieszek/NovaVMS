using NovaVMS.Core.Models;
using NovaVMS.Mobile.Services;
using Xunit;

namespace NovaVMS.Mobile.Tests;

public sealed class PlaybackPolicyTests
{
    [Theory]
    [InlineData(4, 15)]
    [InlineData(9, 12)]
    [InlineData(16, 8)]
    [InlineData(25, 5)]
    public void SoftwareDecodedPreviewsAreLimitedByTheNumberOfPreviews(int previews, int frameRate)
    {
        var options = PlaybackPolicy.ForPreview(new AppSettings(), previews, PlaybackPolicy.HardwareDecodedPreviews);

        Assert.False(options.HardwareDecoding);
        Assert.True(options.LowCostDecoding);
        Assert.False(options.WithAudio);
        Assert.Equal(frameRate, options.MaxFrameRate);
    }

    [Fact]
    public void FirstPreviewsUseTheHardwareDecoderWithoutFilters()
    {
        for (var index = 0; index < PlaybackPolicy.HardwareDecodedPreviews; index++)
        {
            var options = PlaybackPolicy.ForPreview(new AppSettings(), 16, index);

            Assert.True(options.HardwareDecoding);
            Assert.False(options.LowCostDecoding);
            Assert.False(options.WithAudio);
            Assert.Equal(0, options.MaxFrameRate);
        }
    }

    [Fact]
    public void SynchronizedSubStreamLimitWinsOverTheAutomaticOne()
    {
        var settings = new AppSettings
        {
            SubStreamMaxFrameRate = 15
        };

        Assert.Equal(15, PlaybackPolicy.ForPreview(settings, 16, 10).MaxFrameRate);
    }

    [Fact]
    public void SingleCameraKeepsAudioAndTheHardwareDecoder()
    {
        var settings = new AppSettings
        {
            MainStreamCachingMs = 800,
            NetworkCachingMs = 100
        };

        var main = PlaybackPolicy.ForCamera(settings, StreamType.Main);
        var sub = PlaybackPolicy.ForCamera(settings, StreamType.Sub);

        Assert.True(main.WithAudio);
        Assert.True(main.HardwareDecoding);
        Assert.False(main.LowCostDecoding);
        Assert.Equal(800, main.NetworkCachingMs);
        Assert.Equal(PlaybackPolicy.MinimumNetworkCachingMs, sub.NetworkCachingMs);
    }
}
