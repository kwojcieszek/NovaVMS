namespace NovaVMS.Protocols.Common;

/// <summary>
/// Aligns audio timestamps with the video clock in an MPEG program stream. Some Hikvision recorders stamp the
/// audio of the main stream with a clock that is hours away from the video clock; the player synchronizes video
/// to audio and then shows only the first frame. Such audio packets are re-stamped onto the video clock in place.
/// Callback boundaries are independent of packet boundaries. Incomplete packets are retained until the next callback.
/// </summary>
internal sealed class ProgramStreamTimestampAligner
{
    private const long ClockRange = 1L << 33;
    private const long MaxAudioDrift = 5 * 90_000;
    private const byte PackHeader = 0xBA;
    private const byte EndCode = 0xB9;

    private long? _lastVideoTimestamp;
    private long _audioCorrection;
    private byte[] _pending = [];
    private bool _passthrough;
    private const int MaxPendingBytes = 8 * 1024 * 1024;

    public byte[] Process(byte[] chunk)
    {
        if (_passthrough) return chunk;
        var data = chunk;
        if (_pending.Length > 0)
        {
            data = new byte[_pending.Length + chunk.Length];
            _pending.CopyTo(data, 0);
            chunk.CopyTo(data, _pending.Length);
            _pending = [];
        }

        var offset = 0;
        List<(int Offset, int Length)>? leadingAudio = null;
        while (offset + 4 <= data.Length)
        {
            if (!IsPacketStart(data, offset))
            {
                // Transport streams and elementary streams must reach the player unchanged.
                _passthrough = true;
                return data;
            }

            var streamId = data[offset + 3];
            if (streamId == PackHeader)
            {
                if (offset + 14 > data.Length) break;
                var packLength = (data[offset + 4] & 0xC0) == 0x40
                    ? 14 + (data[offset + 13] & 0x07) : 12;
                if (offset + packLength > data.Length) break;
                offset += packLength;
                continue;
            }

            if (streamId == EndCode)
            {
                offset += 4;
                continue;
            }

            if (offset + 6 > data.Length) break;
            var packetLength = 6 + ((data[offset + 4] << 8) | data[offset + 5]);
            if (packetLength == 6 && IsVideo(streamId))
            {
                // A zero-length video PES packet ends at the next program-stream start code.
                var next = offset + 6;
                while (next + 4 <= data.Length && !IsPacketStart(data, next)) next++;
                if (next + 4 > data.Length) break;
                packetLength = next - offset;
            }
            if (offset + packetLength > data.Length) break;

            if (IsVideo(streamId))
            {
                if (TryReadTimestamp(data, offset, packetLength, out var timestamp))
                {
                    _lastVideoTimestamp = timestamp;
                    if (leadingAudio is not null)
                    {
                        foreach (var packet in leadingAudio) AlignAudio(data, packet.Offset, packet.Length);
                        leadingAudio = null;
                    }
                }
            }
            else if (IsAudio(streamId))
            {
                if (_lastVideoTimestamp is null)
                    (leadingAudio ??= []).Add((offset, packetLength));
                else
                    AlignAudio(data, offset, packetLength);
            }

            offset += packetLength;
        }

        // Do not send an unrelated audio clock to the player before the first video timestamp arrives.
        if (_lastVideoTimestamp is null) offset = 0;
        if (data.Length - offset > MaxPendingBytes)
        {
            // Bound memory use even for malformed input without dropping compressed video bytes.
            _passthrough = true;
            return data;
        }
        if (offset == data.Length) return data;
        _pending = data[offset..];
        return data[..offset];
    }

    private static bool IsPacketStart(byte[] data, int offset) =>
        data[offset] == 0 && data[offset + 1] == 0 && data[offset + 2] == 1 && data[offset + 3] >= EndCode;

    private void AlignAudio(byte[] data, int offset, int packetLength)
    {
        if (_lastVideoTimestamp is not { } video || !TryReadTimestamp(data, offset, packetLength, out var timestamp))
        {
            return;
        }

        if (Math.Abs(ToSigned(timestamp - _audioCorrection - video)) > MaxAudioDrift)
        {
            _audioCorrection = ToSigned(timestamp - video);
        }

        if (_audioCorrection == 0)
        {
            return;
        }

        WriteTimestamp(data, offset + 9, timestamp - _audioCorrection);
        if (HasDecodingTimestamp(data, offset, packetLength))
        {
            WriteTimestamp(data, offset + 14, ReadTimestamp(data, offset + 14) - _audioCorrection);
        }
    }

    private static bool IsVideo(byte streamId)
    {
        return streamId is >= 0xE0 and <= 0xEF;
    }

    private static bool IsAudio(byte streamId)
    {
        return streamId is >= 0xC0 and <= 0xDF;
    }

    /// <summary>
    /// Reads the presentation timestamp of an MPEG-2 PES packet that carries one.
    /// </summary>
    private static bool TryReadTimestamp(byte[] data, int offset, int packetLength, out long timestamp)
    {
        timestamp = 0;
        if (packetLength < 14 || (data[offset + 6] & 0xC0) != 0x80 || (data[offset + 7] & 0x80) == 0
            || data[offset + 8] < 5 || 9 + data[offset + 8] > packetLength)
        {
            return false;
        }

        timestamp = ReadTimestamp(data, offset + 9);
        return true;
    }

    private static bool HasDecodingTimestamp(byte[] data, int offset, int packetLength)
    {
        return (data[offset + 7] & 0xC0) == 0xC0 && data[offset + 8] >= 10 && packetLength >= 19;
    }

    private static long ReadTimestamp(byte[] data, int position)
    {
        return ((long)(data[position] >> 1) & 0x07) << 30
            | (long)data[position + 1] << 22
            | (long)(data[position + 2] >> 1) << 15
            | (long)data[position + 3] << 7
            | (long)(data[position + 4] >> 1);
    }

    private static void WriteTimestamp(byte[] data, int position, long timestamp)
    {
        var value = ((timestamp % ClockRange) + ClockRange) % ClockRange;

        // The prefix bits of the first byte are kept; every marker bit is set.
        data[position] = (byte)((long)(data[position] & 0xF0) | ((value >> 29) & 0x0E) | 0x01);
        data[position + 1] = (byte)(value >> 22);
        data[position + 2] = (byte)(((value >> 14) & 0xFE) | 0x01);
        data[position + 3] = (byte)(value >> 7);
        data[position + 4] = (byte)(((value << 1) & 0xFE) | 0x01);
    }

    /// <summary>
    /// Difference of two timestamps on the 33-bit wrapping clock, in the range [-2^32, 2^32).
    /// </summary>
    private static long ToSigned(long difference)
    {
        var value = ((difference % ClockRange) + ClockRange) % ClockRange;
        return value >= ClockRange / 2 ? value - ClockRange : value;
    }
}
