using System.Diagnostics;
using System.Text;
using LibVLCSharp.Shared;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class NativePlaybackLifecycleTests
{
    [Fact]
    public async Task NonSeekableRecordingIsPacedAndCanPauseWithoutConsumingItsRemainingFrames()
    {
        using var vlc = VideoPlayerFactory.CreateRecordingEngine("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        using var player = VideoPlayerFactory.Create(vlc);
        var bytes = CreateVideo(250); // Ten seconds, available immediately like a fast SDK download.
        using var media = VideoMedia.Create(vlc, VideoSource.FromStream(() => new NonSeekableStream(bytes)),
            "", "", 0, withAudio: false, paceStream: true);
        try
        {
            Assert.True(player.Play(media.Media));
            var timeout = Stopwatch.StartNew();
            while (media.Media.Statistics.DisplayedPictures == 0 && timeout.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(20);
            Assert.True(media.Media.Statistics.DisplayedPictures > 0);
            await Task.Delay(700);
            Assert.True(player.IsPlaying);
            Assert.InRange(media.Media.Statistics.DecodedVideo, 1, 100);
            player.SetPause(true);
            await Task.Delay(300);
            var frames = media.Media.Statistics.DisplayedPictures;
            await Task.Delay(400);
            Assert.Equal(VLCState.Paused, player.State);
            Assert.InRange(media.Media.Statistics.DisplayedPictures - frames, 0, 1);
            player.SetPause(false);
            await Task.Delay(400);
            Assert.Equal(VLCState.Playing, player.State);
            Assert.True(media.Media.Statistics.DisplayedPictures > frames);
        }
        finally { VideoMedia.Stop(player, media); }
    }

    [Fact]
    public async Task RepeatedPlaybackDecodesFramesAndStopsBeforeReleasingStreamCallbacks()
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        using var player = VideoPlayerFactory.Create(vlc);
        var bytes = CreateVideo();
        for (var cycle = 0; cycle < 30; cycle++)
        {
            var stream = new MemoryStream(bytes, writable: false);
            using var media = VideoMedia.Create(vlc, VideoSource.FromStream(() => stream), "", "", 0, withAudio: false);
            try
            {
                Assert.True(player.Play(media.Media));
                var timeout = Stopwatch.StartNew();
                while (media.Media.Statistics.DecodedVideo == 0 && timeout.Elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(20);
                Assert.True(media.Media.Statistics.DecodedVideo > 0, $"No decoded frame in cycle {cycle}; state={player.State}");
            }
            finally { VideoMedia.Stop(player, media); }
            Assert.False(stream.CanRead);
            Assert.False(player.IsPlaying);
        }
    }

    internal static byte[] CreateVideo(int frames = 50)
    {
        using var video = new MemoryStream();
        video.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W64 H48 F25:1 Ip A1:1 C420jpeg\n"));
        var frame = Enumerable.Repeat((byte)128, 64 * 48 * 3 / 2).ToArray();
        for (var index = 0; index < frames; index++)
        {
            video.Write("FRAME\n"u8);
            video.Write(frame);
        }
        return video.ToArray();
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _stream = new(bytes, writable: false);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => -1;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _stream.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        protected override void Dispose(bool disposing) { if (disposing) _stream.Dispose(); base.Dispose(disposing); }
    }
}
