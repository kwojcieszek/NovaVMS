using System.Buffers.Binary;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>
/// One coded picture received over RTP: its NAL units without start codes, and the parameter sets
/// (VPS for H.265, SPS, PPS) needed to decode it. Parameter set NAL units are never part of <see cref="Nals"/>.
/// The parameter set list is replaced, never changed, so a decoder can compare it by reference.
/// </summary>
public sealed class VideoAccessUnit(VideoCodec codec, IReadOnlyList<byte[]> nals, IReadOnlyList<byte[]> parameterSets, bool isKeyFrame, long timestamp)
{
    public VideoCodec Codec { get; } = codec;

    public IReadOnlyList<byte[]> Nals { get; } = nals;

    public IReadOnlyList<byte[]> ParameterSets { get; } = parameterSets;

    /// <summary>A picture that can be decoded without earlier ones (IDR in H.264, IRAP in H.265).</summary>
    public bool IsKeyFrame { get; } = isKeyFrame;

    /// <summary>Presentation time in 90 kHz ticks, counted from the first picture of the stream.</summary>
    public long Timestamp { get; } = timestamp;

    public int Size => Nals.Sum(nal => nal.Length);

    /// <summary>NAL units each preceded by a 4-byte big-endian length (AVCC/HVCC), as Apple decoders expect.</summary>
    public byte[] ToLengthPrefixed()
    {
        var result = new byte[Nals.Count * 4 + Size];
        var offset = 0;

        foreach (var nal in Nals)
        {
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(offset), nal.Length);
            nal.CopyTo(result.AsSpan(offset + 4));
            offset += 4 + nal.Length;
        }

        return result;
    }

    /// <summary>NAL units each preceded by a 00 00 00 01 start code (Annex B), as Android decoders expect.</summary>
    public int CopyAnnexB(Span<byte> destination, bool withParameterSets)
    {
        var offset = 0;

        if (withParameterSets)
        {
            foreach (var parameter in ParameterSets)
            {
                offset += CopyNal(parameter, destination[offset..]);
            }
        }

        foreach (var nal in Nals)
        {
            offset += CopyNal(nal, destination[offset..]);
        }

        return offset;
    }

    /// <summary>Bytes needed by <see cref="CopyAnnexB"/>.</summary>
    public int AnnexBSize(bool withParameterSets)
    {
        var size = Nals.Count * 4 + Size;

        return withParameterSets ? size + ParameterSets.Sum(parameter => parameter.Length + 4) : size;
    }

    private static int CopyNal(byte[] nal, Span<byte> destination)
    {
        destination[0] = 0;
        destination[1] = 0;
        destination[2] = 0;
        destination[3] = 1;
        nal.CopyTo(destination[4..]);

        return nal.Length + 4;
    }
}
