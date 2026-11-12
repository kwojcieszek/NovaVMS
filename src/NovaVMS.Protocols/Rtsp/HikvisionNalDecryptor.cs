using System.Security.Cryptography;
using System.Text;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>
/// Decrypts NAL units of Hikvision stream encryption without the vendor decoder. The key is the verification
/// code of the recorder (UTF-8, at most 16 bytes, padded with zeros); the first whole 16-byte blocks after
/// the clear NAL header, at most 4 KB, are encrypted with AES-128-ECB. In H.264 the encrypted data is the
/// original NAL unit including its header (so the clear header is followed by a copy of it); in H.265 it is
/// the payload after the header. Packets of encrypted NAL units carry the RTP header extension 0x4000.
/// </summary>
internal sealed class HikvisionNalDecryptor : IDisposable
{
    internal const ushort ExtensionProfile = 0x4000;

    private const int BlockSize = 16;
    private const int MaxEncryptedBytes = 4096;
    private const int H265VideoParameterSet = 32;

    private readonly Aes _aes;
    private readonly bool _hevc;
    private readonly int _headerSize;
    private readonly byte[] _block = new byte[BlockSize];
    private bool _flagSeen;

    /// <summary>An encrypted NAL unit did not decrypt with the key: the key is wrong.</summary>
    public bool KeyRejected { get; private set; }

    public HikvisionNalDecryptor(string key, VideoCodec codec)
    {
        if (string.IsNullOrEmpty(key) || key.Contains('\0'))
        {
            throw new ArgumentException("The stream key must not be empty or contain a null character.", nameof(key));
        }

        var bytes = new byte[BlockSize];
        var encoded = Encoding.UTF8.GetBytes(key);

        encoded.AsSpan(0, Math.Min(encoded.Length, BlockSize)).CopyTo(bytes);
        CryptographicOperations.ZeroMemory(encoded);

        _aes = Aes.Create();

        try
        {
            _aes.Key = bytes;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        _hevc = codec == VideoCodec.H265;
        _headerSize = _hevc ? 2 : 1;
    }

    /// <summary>
    /// Returns the clear NAL unit, or null when <paramref name="nal"/> is not encrypted with this key.
    /// Once a packet has been flagged as encrypted, unflagged NAL units are passed through unchanged.
    /// </summary>
    public byte[]? Decrypt(ReadOnlySpan<byte> nal, bool flagged)
    {
        if (flagged)
        {
            _flagSeen = true;
        }
        else if (_flagSeen || _hevc)
        {
            // Unflagged H.265 data cannot be recognized as encrypted.
            return null;
        }

        if (nal.Length < _headerSize + BlockSize)
        {
            return null;
        }

        var body = nal[_headerSize..];
        var count = Math.Min(body.Length / BlockSize * BlockSize, MaxEncryptedBytes);

        _aes.DecryptEcb(body[..BlockSize], _block, PaddingMode.None);

        if (!IsValid(nal))
        {
            KeyRejected |= flagged;

            return null;
        }

        // H.264 drops the clear header: the decrypted data starts with the original one.
        var prefix = _hevc ? _headerSize : 0;
        var plain = new byte[prefix + body.Length];

        nal[..prefix].CopyTo(plain);
        _aes.DecryptEcb(body[..count], plain.AsSpan(prefix, count), PaddingMode.None);
        body[count..].CopyTo(plain.AsSpan(prefix + count));

        return plain;
    }

    public void Dispose()
    {
        _aes.Dispose();
    }

    // Checks the decrypted first block in _block.
    private bool IsValid(ReadOnlySpan<byte> nal)
    {
        if (!_hevc)
        {
            return _block[0] == nal[0];
        }

        // A video parameter set has the reserved field 0xFFFF in its third and fourth bytes;
        // other H.265 NAL units have nothing fixed to compare.
        return ((nal[0] >> 1) & 63) != H265VideoParameterSet || (_block[2] == 0xff && _block[3] == 0xff);
    }
}
