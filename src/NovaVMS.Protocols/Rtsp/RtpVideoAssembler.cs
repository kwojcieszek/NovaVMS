using System.Buffers.Binary;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>
/// RFC 6184 (H.264) and RFC 7798 (H.265) depacketization into whole pictures. Live video over a mobile
/// network loses data now and then: a damaged picture is dropped and pictures are skipped until the next key
/// frame, instead of ending the stream, so the decoder never receives a picture it cannot decode.
/// </summary>
public sealed class RtpVideoAssembler
{
    private const int MaxPictureSize = 8 * 1024 * 1024;

    private readonly VideoCodec _codec;
    private readonly int _payloadType;
    private readonly Action<VideoAccessUnit> _output;
    private readonly List<byte[]> _nals = [];
    private readonly MemoryStream _fragment = new();
    private byte[]?[] _parameters;
    private IReadOnlyList<byte[]> _parameterList;
    private ushort _expectedSequence;
    private bool _hasSequence;
    private bool _pictureDamaged;
    private bool _waitForKeyFrame = true;
    private bool _keyFrame;
    private int _pictureSize;
    private uint _pictureTimestamp;
    private bool _hasPicture;
    private uint? _firstTimestamp;
    private uint _previousTimestamp;
    private long _ticks;

    public RtpVideoAssembler(VideoCodec codec, int payloadType, IEnumerable<byte[]> parameterSets, Action<VideoAccessUnit> output)
    {
        _codec = codec;
        _payloadType = payloadType;
        _output = output;
        _parameters = new byte[3][];
        _parameterList = [];

        foreach (var parameter in parameterSets)
        {
            if (parameter.Length > 0 && ParameterSlot(NalType(parameter)) is { } slot)
            {
                _parameters[slot] = parameter;
            }
        }

        _parameterList = CurrentParameters();
    }

    /// <summary>Pictures dropped because data was lost; for diagnostics.</summary>
    public int DroppedPictures { get; private set; }

    /// <summary>Processes one RTP packet; the span is only read during the call.</summary>
    public void Accept(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 12 || (packet[0] >> 6) != 2 || (packet[1] & 0x7f) != _payloadType)
        {
            return;
        }

        var offset = 12 + (packet[0] & 15) * 4;

        if ((packet[0] & 0x10) != 0)
        {
            if (offset + 4 > packet.Length)
            {
                return;
            }

            offset += 4 + BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]) * 4;
        }

        var end = packet.Length;

        if ((packet[0] & 0x20) != 0)
        {
            end -= packet[^1];
        }

        var sequence = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        var timestamp = BinaryPrimitives.ReadUInt32BigEndian(packet[4..]);
        var marker = (packet[1] & 0x80) != 0;

        if (_hasSequence && sequence != _expectedSequence)
        {
            Lost();
        }

        _hasSequence = true;
        _expectedSequence = unchecked((ushort)(sequence + 1));

        if (_hasPicture && timestamp != _pictureTimestamp)
        {
            // The marker bit of the previous picture was lost or not sent.
            Complete();
        }

        if (!_hasPicture)
        {
            _hasPicture = true;
            _pictureTimestamp = timestamp;
        }

        if (offset < end)
        {
            Depacketize(packet[offset..end]);
        }
        else
        {
            _pictureDamaged = true;
        }

        if (marker)
        {
            Complete();
        }
    }

    private void Depacketize(ReadOnlySpan<byte> payload)
    {
        var hevc = _codec == VideoCodec.H265;
        var headerSize = hevc ? 2 : 1;

        if (payload.Length < headerSize)
        {
            _pictureDamaged = true;

            return;
        }

        var type = hevc ? (payload[0] >> 1) & 63 : payload[0] & 31;

        if (type == (hevc ? 48 : 24))
        {
            // Aggregation packet (STAP-A / AP): NAL units with 16-bit sizes.
            var index = headerSize;

            while (index + 2 <= payload.Length)
            {
                var size = BinaryPrimitives.ReadUInt16BigEndian(payload[index..]);

                index += 2;

                if (size == 0 || index + size > payload.Length)
                {
                    _pictureDamaged = true;

                    return;
                }

                AddNal(payload.Slice(index, size));
                index += size;
            }

            return;
        }

        if (type == (hevc ? 49 : 28))
        {
            // Fragmentation unit (FU-A / FU).
            var fuSize = headerSize + 1;

            if (payload.Length <= fuSize)
            {
                _pictureDamaged = true;

                return;
            }

            var fu = payload[headerSize];
            var start = (fu & 0x80) != 0;
            var finish = (fu & 0x40) != 0;

            if (start)
            {
                _fragment.SetLength(0);

                if (hevc)
                {
                    _fragment.WriteByte((byte)((payload[0] & 0x81) | ((fu & 63) << 1)));
                    _fragment.WriteByte(payload[1]);
                }
                else
                {
                    _fragment.WriteByte((byte)((payload[0] & 0xe0) | (fu & 31)));
                }
            }
            else if (_fragment.Length == 0)
            {
                // The start of this NAL unit was lost.
                _pictureDamaged = true;

                return;
            }

            if (_fragment.Length + payload.Length > MaxPictureSize)
            {
                _fragment.SetLength(0);
                _pictureDamaged = true;

                return;
            }

            _fragment.Write(payload[fuSize..]);

            if (finish)
            {
                AddNal(_fragment.GetBuffer().AsSpan(0, (int)_fragment.Length));
                _fragment.SetLength(0);
            }

            return;
        }

        if ((!hevc && type is > 0 and < 24) || (hevc && type < 48))
        {
            AddNal(payload);
        }
    }

    private void AddNal(ReadOnlySpan<byte> nal)
    {
        var type = NalType(nal);

        if (ParameterSlot(type) is { } slot)
        {
            if (_parameters[slot] is not { } known || !nal.SequenceEqual(known))
            {
                _parameters[slot] = nal.ToArray();
                _parameterList = CurrentParameters();
            }

            return;
        }

        // Access unit delimiters carry nothing a decoder needs.
        if (type == (_codec == VideoCodec.H265 ? 35 : 9))
        {
            return;
        }

        _pictureSize += nal.Length;

        if (_pictureSize > MaxPictureSize)
        {
            _pictureDamaged = true;

            return;
        }

        _keyFrame |= _codec == VideoCodec.H265 ? type is >= 16 and <= 21 : type == 5;
        _nals.Add(nal.ToArray());
    }

    private void Complete()
    {
        if (_fragment.Length > 0)
        {
            // A fragmented NAL unit lost its end.
            _pictureDamaged = true;
            _fragment.SetLength(0);
        }

        if (_hasPicture && _nals.Count > 0)
        {
            _firstTimestamp ??= _previousTimestamp = _pictureTimestamp;
            // Signed deltas keep the order of reordered pictures and handle the 32-bit wraparound.
            _ticks += unchecked((int)(_pictureTimestamp - _previousTimestamp));
            _previousTimestamp = _pictureTimestamp;

            if (_pictureDamaged)
            {
                DroppedPictures++;
                _waitForKeyFrame = true;
            }
            else if (_keyFrame || !_waitForKeyFrame)
            {
                if (_keyFrame && HasParameters())
                {
                    _waitForKeyFrame = false;
                }

                if (!_waitForKeyFrame)
                {
                    _output(new VideoAccessUnit(_codec, _nals.ToArray(), _parameterList, _keyFrame, _ticks));
                }
            }
        }

        _nals.Clear();
        _pictureSize = 0;
        _keyFrame = false;
        _pictureDamaged = false;
        _hasPicture = false;
    }

    private void Lost()
    {
        _pictureDamaged = true;
        _waitForKeyFrame = true;
    }

    private bool HasParameters()
    {
        return _codec == VideoCodec.H265
            ? _parameters[0] is not null && _parameters[1] is not null && _parameters[2] is not null
            : _parameters[1] is not null && _parameters[2] is not null;
    }

    private IReadOnlyList<byte[]> CurrentParameters()
    {
        return _parameters.Where(parameter => parameter is not null).Select(parameter => parameter!).ToArray();
    }

    private int NalType(ReadOnlySpan<byte> nal)
    {
        return _codec == VideoCodec.H265 ? (nal[0] >> 1) & 63 : nal[0] & 31;
    }

    // Slots in decoder order: VPS (H.265 only), SPS, PPS.
    private int? ParameterSlot(int type)
    {
        return _codec == VideoCodec.H265
            ? type switch { 32 => 0, 33 => 1, 34 => 2, _ => null }
            : type switch { 7 => 1, 8 => 2, _ => null };
    }
}
