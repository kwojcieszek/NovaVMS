using NovaVMS.Protocols.Rtsp;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class MpegTsVideoWriterTests
{
    private static readonly byte[] Sps = [0x67, 0x42, 0x00, 0x1f];
    private static readonly byte[] Pps = [0x68, 0xce, 0x3c, 0x80];

    [Fact]
    public void WritesTablesAndVideoPacketsForKeyFrame()
    {
        var writer = new MpegTsVideoWriter(VideoCodec.H264);
        var idr = new byte[1000];

        idr[0] = 0x65;

        var unit = new VideoAccessUnit(VideoCodec.H264, [idr], [Sps, Pps], isKeyFrame: true, timestamp: 3600);
        var stream = writer.Write(unit);

        Assert.Equal(0, stream.Length % 188);
        Assert.All(Enumerable.Range(0, stream.Length / 188), index => Assert.Equal(0x47, stream[index * 188]));

        // PAT, PMT, then the video PID with the payload unit start flag.
        Assert.Equal(0x0000, Pid(stream, 0));
        Assert.Equal(0x1000, Pid(stream, 1));
        Assert.Equal(0x0100, Pid(stream, 2));
        Assert.True((stream[2 * 188 + 1] & 0x40) != 0);

        // A section followed by its CRC gives a zero CRC.
        var patLength = ((stream[6] & 0x0f) << 8) | stream[7];

        Assert.Equal(0u, MpegTsVideoWriter.Crc32(stream.AsSpan(5, 3 + patLength)));
        Assert.Equal(0x1b, stream[188 + 5 + 12]);
    }

    [Fact]
    public void WritesOnlyVideoForOtherFrames()
    {
        var writer = new MpegTsVideoWriter(VideoCodec.H265);
        var slice = new byte[] { 0x02, 0x01, 0xd0, 0x00 };
        var unit = new VideoAccessUnit(VideoCodec.H265, [slice], [], isKeyFrame: false, timestamp: 0);
        var stream = writer.Write(unit);

        var packet = Assert.Single(Enumerable.Range(0, stream.Length / 188));

        Assert.Equal(0x0100, Pid(stream, packet));
    }

    private static int Pid(byte[] stream, int packet)
    {
        return ((stream[packet * 188 + 1] & 0x1f) << 8) | stream[packet * 188 + 2];
    }
}
