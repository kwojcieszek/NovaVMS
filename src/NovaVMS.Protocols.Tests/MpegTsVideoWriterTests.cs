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

    [Fact]
    public void EveryPesPacketHasItsExactLength()
    {
        var writer = new MpegTsVideoWriter(VideoCodec.H264);
        var slice = new byte[5000];

        slice[0] = 0x41;

        var unit = new VideoAccessUnit(VideoCodec.H264, [slice], [], isKeyFrame: false, timestamp: 3600);
        var pes = Assert.Single(VideoPes(writer.Write(unit)));

        Assert.Equal(pes.Length - 6, (pes[4] << 8) | pes[5]);
        Assert.Equal(0x80, pes[7]);
    }

    [Fact]
    public void LargePictureSpansBoundedPesPacketsWithOneTimestamp()
    {
        var writer = new MpegTsVideoWriter(VideoCodec.H265);
        var idr = new byte[200_000];

        idr[0] = 0x26;
        idr[1] = 0x01;

        var unit = new VideoAccessUnit(VideoCodec.H265, [idr], [], isKeyFrame: true, timestamp: 90000);
        var packets = VideoPes(writer.Write(unit));

        Assert.True(packets.Count > 1);
        Assert.All(packets, pes => Assert.Equal(pes.Length - 6, (pes[4] << 8) | pes[5]));
        Assert.Equal(0x80, packets[0][7]);
        Assert.All(packets.Skip(1), pes => Assert.Equal(0, pes[7]));

        var payload = packets.Sum(pes => pes.Length - 9 - pes[8]);

        Assert.Equal(7 + 4 + idr.Length, payload);
    }

    [Fact]
    public void ContinuityCounterAdvancesOnEveryVideoPacket()
    {
        var writer = new MpegTsVideoWriter(VideoCodec.H264);
        var slice = new byte[150_000];

        slice[0] = 0x41;

        var stream = writer.Write(new VideoAccessUnit(VideoCodec.H264, [slice], [], isKeyFrame: false, timestamp: 0));
        var counters = Enumerable.Range(0, stream.Length / 188).Select(index => stream[index * 188 + 3] & 15).ToArray();

        Assert.All(Enumerable.Range(1, counters.Length - 1), index => Assert.Equal((counters[index - 1] + 1) & 15, counters[index]));
    }

    private static List<byte[]> VideoPes(byte[] stream)
    {
        var packets = new List<byte[]>();
        List<byte>? current = null;

        for (var offset = 0; offset < stream.Length; offset += 188)
        {
            if (Pid(stream, offset / 188) != 0x0100)
            {
                continue;
            }

            var start = 4;

            if ((stream[offset + 3] & 0x20) != 0)
            {
                start += 1 + stream[offset + 4];
            }

            if ((stream[offset + 1] & 0x40) != 0)
            {
                if (current is not null)
                {
                    packets.Add(current.ToArray());
                }

                current = [];
            }

            current!.AddRange(stream.AsSpan(offset + start, 188 - start).ToArray());
        }

        if (current is not null)
        {
            packets.Add(current.ToArray());
        }

        return packets;
    }

    private static int Pid(byte[] stream, int packet)
    {
        return ((stream[packet * 188 + 1] & 0x1f) << 8) | stream[packet * 188 + 2];
    }
}
