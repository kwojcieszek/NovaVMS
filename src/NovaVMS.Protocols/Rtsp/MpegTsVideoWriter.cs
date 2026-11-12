using System.Buffers.Binary;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>
/// Packs H.264/H.265 pictures into an MPEG transport stream with presentation times from RTP, so LibVLC
/// recognizes the format and plays the pictures at their own pace. Tables are repeated before every key frame.
/// </summary>
internal sealed class MpegTsVideoWriter
{
    private const int PacketSize = 188;
    private const int PmtPid = 0x1000;
    private const int VideoPid = 0x100;
    private const int ProgramNumber = 1;

    // Presentation times start after the clock reference, leaving room for pictures sent out of order.
    private const long PresentationDelay = 9000;
    private const long TimestampMask = (1L << 33) - 1;

    private static readonly byte[] H264Delimiter = [0, 0, 0, 1, 0x09, 0xf0];
    private static readonly byte[] H265Delimiter = [0, 0, 0, 1, 0x46, 0x01, 0x50];

    private readonly byte[] _pat;
    private readonly byte[] _pmt;
    private readonly byte[] _delimiter;
    private int _patCounter;
    private int _pmtCounter;
    private int _videoCounter;

    public MpegTsVideoWriter(VideoCodec codec)
    {
        _pat = BuildSection(0x00, [(byte)(ProgramNumber >> 8), ProgramNumber, (byte)(0xe0 | (PmtPid >> 8)), PmtPid & 0xff], tableIdExtension: 1);
        _pmt = BuildSection(0x02,
        [
            (byte)(0xe0 | (VideoPid >> 8)), VideoPid & 0xff, 0xf0, 0x00,
            codec == VideoCodec.H265 ? (byte)0x24 : (byte)0x1b, (byte)(0xe0 | (VideoPid >> 8)), VideoPid & 0xff, 0xf0, 0x00
        ], tableIdExtension: ProgramNumber);
        _delimiter = codec == VideoCodec.H265 ? H265Delimiter : H264Delimiter;
    }

    /// <summary>Returns the transport stream packets of one picture; parameter sets precede every key frame.</summary>
    public byte[] Write(VideoAccessUnit unit)
    {
        var clock = unit.Timestamp & TimestampMask;
        var presentation = (unit.Timestamp + PresentationDelay) & TimestampMask;
        var elementary = new byte[_delimiter.Length + unit.AnnexBSize(unit.IsKeyFrame)];

        _delimiter.CopyTo(elementary, 0);
        unit.CopyAnnexB(elementary.AsSpan(_delimiter.Length), unit.IsKeyFrame);

        var pes = new byte[14 + elementary.Length];

        BuildPesHeader(pes, presentation);
        elementary.CopyTo(pes, 14);

        using var output = new MemoryStream();

        if (unit.IsKeyFrame)
        {
            WriteSection(output, 0, _pat, ref _patCounter);
            WriteSection(output, PmtPid, _pmt, ref _pmtCounter);
        }

        WritePes(output, pes, clock, unit.IsKeyFrame);

        return output.ToArray();
    }

    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;

        foreach (var value in data)
        {
            crc ^= (uint)value << 24;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04c11db7 : crc << 1;
            }
        }

        return crc;
    }

    private static byte[] BuildSection(byte tableId, byte[] body, int tableIdExtension)
    {
        // PMT bodies start with the PCR PID and an empty program info loop.
        var section = new byte[8 + body.Length + 4];
        var length = section.Length - 3;

        section[0] = tableId;
        section[1] = (byte)(0xb0 | (length >> 8));
        section[2] = (byte)length;
        section[3] = (byte)(tableIdExtension >> 8);
        section[4] = (byte)tableIdExtension;
        section[5] = 0xc1;
        section[6] = 0;
        section[7] = 0;
        body.CopyTo(section, 8);

        var crc = Crc32(section.AsSpan(0, section.Length - 4));

        BinaryPrimitives.WriteUInt32BigEndian(section.AsSpan(section.Length - 4), crc);

        return section;
    }

    private static void BuildPesHeader(byte[] pes, long presentation)
    {
        pes[0] = 0;
        pes[1] = 0;
        pes[2] = 1;
        pes[3] = 0xe0;
        // Length 0: unbounded video packet.
        pes[4] = 0;
        pes[5] = 0;
        pes[6] = 0x80;
        pes[7] = 0x80;
        pes[8] = 5;
        pes[9] = (byte)(0x21 | ((presentation >> 29) & 0x0e));
        pes[10] = (byte)(presentation >> 22);
        pes[11] = (byte)(((presentation >> 14) & 0xfe) | 1);
        pes[12] = (byte)(presentation >> 7);
        pes[13] = (byte)(((presentation << 1) & 0xfe) | 1);
    }

    private static void WriteSection(Stream output, int pid, byte[] section, ref int counter)
    {
        var packet = new byte[PacketSize];

        packet.AsSpan().Fill(0xff);
        WriteHeader(packet, pid, start: true, ref counter, adaptation: false);
        packet[4] = 0;
        section.CopyTo(packet, 5);
        output.Write(packet);
    }

    private static void WriteHeader(byte[] packet, int pid, bool start, ref int counter, bool adaptation)
    {
        packet[0] = 0x47;
        packet[1] = (byte)((start ? 0x40 : 0) | (pid >> 8));
        packet[2] = (byte)pid;
        packet[3] = (byte)((adaptation ? 0x30 : 0x10) | counter);
        counter = (counter + 1) & 15;
    }

    private void WritePes(Stream output, byte[] pes, long clock, bool keyFrame)
    {
        var offset = 0;
        var first = true;

        while (offset < pes.Length)
        {
            var packet = new byte[PacketSize];
            var remaining = pes.Length - offset;
            // The first packet carries the clock reference (and the random access flag of a key frame).
            var adaptationLength = first ? 8 : 0;
            var payload = Math.Min(remaining, PacketSize - 4 - adaptationLength);

            if (payload < PacketSize - 4 - adaptationLength)
            {
                // The last packet is filled up with stuffing bytes of the adaptation field.
                adaptationLength = PacketSize - 4 - payload;
            }

            WriteHeader(packet, VideoPid, first, ref _videoCounter, adaptationLength > 0);

            if (adaptationLength > 0)
            {
                WriteAdaptation(packet.AsSpan(4, adaptationLength), first ? clock : null, first && keyFrame);
            }

            pes.AsSpan(offset, payload).CopyTo(packet.AsSpan(4 + adaptationLength));
            output.Write(packet);

            offset += payload;
            first = false;
        }
    }

    private static void WriteAdaptation(Span<byte> field, long? clock, bool randomAccess)
    {
        field[0] = (byte)(field.Length - 1);

        if (field.Length == 1)
        {
            return;
        }

        field[1] = (byte)((randomAccess ? 0x40 : 0) | (clock is null ? 0 : 0x10));
        field[2..].Fill(0xff);

        if (clock is { } value)
        {
            field[2] = (byte)(value >> 25);
            field[3] = (byte)(value >> 17);
            field[4] = (byte)(value >> 9);
            field[5] = (byte)(value >> 1);
            field[6] = (byte)(((value & 1) << 7) | 0x7e);
            field[7] = 0;
        }
    }
}
