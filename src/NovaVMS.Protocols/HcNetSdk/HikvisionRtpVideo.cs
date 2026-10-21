using System.Buffers.Binary;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// RFC 6184/7798 video depacketization. NAL bytes (including encrypted slice bytes)
/// remain unchanged. Elementary streams are wrapped in MPEG-2 PES for PlayCtrl.
/// MPEG program streams carried over RTP are passed through after removing RTP headers.
/// </summary>
internal sealed class HikvisionRtpVideo(string codec, int payloadType, IReadOnlyList<byte[]> parameterSets, Action<byte[]> output)
{
    private readonly MemoryStream _fragment = new();
    private ushort _nextSequence;
    private uint _fragmentTimestamp;
    private uint? _initialTimestamp;
    private uint _previousTimestamp;
    private long _ticks = 90000;
    private bool _started;

    /// <summary>
    /// Processes one RTP packet. The span is only read during the call; kept data is copied.
    /// </summary>
    public void Accept(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 12 || (packet[0] >> 6) != 2) throw new IOException("Invalid RTP packet.");
        if ((packet[1] & 0x7f) != payloadType) return;
        var offset = 12 + (packet[0] & 15) * 4;
        if ((packet[0] & 0x10) != 0)
        {
            if (offset + 4 > packet.Length) throw new IOException("Invalid RTP extension.");
            offset += 4 + BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]) * 4;
        }
        var end = packet.Length;
        if ((packet[0] & 0x20) != 0)
        {
            var padding = packet[^1];
            if (padding == 0 || padding > end - offset) throw new IOException("Invalid RTP padding.");
            end -= padding;
        }
        if (offset >= end) throw new IOException("Empty RTP video packet.");
        var timestamp = BinaryPrimitives.ReadUInt32BigEndian(packet[4..]);
        var sequence = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if (_initialTimestamp is null) _initialTimestamp = _previousTimestamp = timestamp;
        // Signed deltas preserve B-frame presentation order and handle 32-bit RTP wraparound.
        _ticks += unchecked((int)(timestamp - _previousTimestamp));
        _previousTimestamp = timestamp;
        var payload = packet.Slice(offset, end - offset);
        if (codec is "MP2P" or "PS") { output(payload.ToArray()); return; }
        if (!_started)
        {
            foreach (var parameter in parameterSets) SendNal(parameter);
            _started = true;
        }
        var hevc = codec is "H265" or "HEVC";
        var type = hevc ? (payload[0] >> 1) & 63 : payload[0] & 31;
        var aggregation = hevc ? 48 : 24;
        var fragmentation = hevc ? 49 : 28;
        if (type == aggregation)
        {
            if (_fragment.Length > 0) throw new IOException("Interrupted RTP fragment.");
            var index = hevc ? 2 : 1;
            while (index < payload.Length)
            {
                if (index + 2 > payload.Length) throw new IOException("Invalid aggregated NAL.");
                var size = BinaryPrimitives.ReadUInt16BigEndian(payload[index..]);
                index += 2;
                if (size == 0 || index + size > payload.Length) throw new IOException("Invalid aggregated NAL size.");
                SendNal(payload.Slice(index, size));
                index += size;
            }
        }
        else if (type == fragmentation)
        {
            var headerSize = hevc ? 3 : 2;
            if (payload.Length <= headerSize) throw new IOException("Invalid fragmented NAL.");
            var fu = payload[headerSize - 1];
            var start = (fu & 0x80) != 0;
            var finish = (fu & 0x40) != 0;
            if (start && finish) throw new IOException("Invalid fragment flags.");
            if (start)
            {
                if (_fragment.Length != 0) throw new IOException("Interrupted RTP fragment.");
                _fragmentTimestamp = timestamp;
                if (hevc)
                {
                    _fragment.WriteByte((byte)((payload[0] & 0x81) | ((fu & 63) << 1)));
                    _fragment.WriteByte(payload[1]);
                }
                else _fragment.WriteByte((byte)((payload[0] & 0xe0) | (fu & 31)));
            }
            else if (_fragment.Length == 0 || sequence != _nextSequence || timestamp != _fragmentTimestamp)
                throw new IOException("Missing or reordered RTP fragment.");
            _nextSequence = unchecked((ushort)(sequence + 1));
            if (_fragment.Length + payload.Length - headerSize > 4 * 1024 * 1024)
                throw new IOException("RTP video frame is too large.");
            _fragment.Write(payload[headerSize..]);
            if (finish)
            {
                SendNal(_fragment.GetBuffer().AsSpan(0, (int)_fragment.Length));
                _fragment.SetLength(0);
            }
        }
        else
        {
            if ((!hevc && type is not (> 0 and < 24)) || (hevc && (type >= 48 || payload.Length < 2)))
                throw new IOException("Unsupported RTP packetization mode.");
            if (_fragment.Length > 0) throw new IOException("Interrupted RTP fragment.");
            SendNal(payload);
        }
    }

    private void SendNal(ReadOnlySpan<byte> nal)
    {
        var elementary = new byte[nal.Length + 4];
        elementary[3] = 1;
        nal.CopyTo(elementary.AsSpan(4));
        output(WrapPes(elementary, unchecked((ulong)_ticks)));
    }

    internal static byte[] WrapPes(ReadOnlySpan<byte> elementary, ulong timestamp)
    {
        using var result = new MemoryStream();
        // MPEG-2 pack header, SCR=0, mux rate=25200. PES carries the RTP-derived presentation time.
        result.Write(Convert.FromHexString("000001BA4400040004010189C3F8"));
        for (var offset = 0; offset < elementary.Length; offset += 60000)
        {
            var size = Math.Min(60000, elementary.Length - offset);
            var header = new byte[14] { 0, 0, 1, 0xe0, 0, 0, 0x80, 0x80, 5, 0, 0, 0, 0, 0 };
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(size + 8));
            var pts = timestamp & 0x1ffffffff;
            header[9] = (byte)(0x21 | ((pts >> 29) & 0xe));
            header[10] = (byte)(pts >> 22);
            header[11] = (byte)(((pts >> 14) & 0xfe) | 1);
            header[12] = (byte)(pts >> 7);
            header[13] = (byte)(((pts << 1) & 0xfe) | 1);
            result.Write(header);
            result.Write(elementary.Slice(offset, size));
        }
        return result.ToArray();
    }
}
