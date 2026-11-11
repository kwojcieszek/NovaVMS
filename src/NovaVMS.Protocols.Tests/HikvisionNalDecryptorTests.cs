using System.Security.Cryptography;
using System.Text;
using NovaVMS.Protocols.Rtsp;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class HikvisionNalDecryptorTests
{
    private const string Key = "ABCDEF";

    private static readonly byte[] Sps = [0x67, 0x42, 0x00, 0x1f];
    private static readonly byte[] Pps = [0x68, 0xce, 0x3c, 0x80];

    [Fact]
    public void DecryptsFlaggedH264NalUnit()
    {
        var idr = Nal([0x65], 5000);
        var pictures = new List<VideoAccessUnit>();

        using var assembler = new RtpVideoAssembler(VideoCodec.H264, 96, [Sps, Pps], pictures.Add, Key);

        assembler.Accept(FlaggedRtp(1, 0, EncryptH264(idr, Key)));

        var picture = Assert.Single(pictures);

        Assert.Equal(idr, Assert.Single(picture.Nals));
        Assert.False(assembler.StreamKeyRejected);
    }

    [Fact]
    public void DecryptsFlaggedHevcNalUnits()
    {
        var vps = Nal([0x40, 0x01, 0x0c, 0x01, 0xff, 0xff], 30);
        var idr = Nal([19 << 1, 0x01], 300);

        using var decryptor = new HikvisionNalDecryptor(Key, VideoCodec.H265);

        Assert.Equal(vps, decryptor.Decrypt(EncryptH265(vps, Key), flagged: true));
        Assert.Equal(idr, decryptor.Decrypt(EncryptH265(idr, Key), flagged: true));
        Assert.False(decryptor.KeyRejected);
    }

    [Fact]
    public void LeavesClearNalUnitsUnchanged()
    {
        var slice = Nal([0x41], 200);

        using var decryptor = new HikvisionNalDecryptor(Key, VideoCodec.H264);

        Assert.Null(decryptor.Decrypt(slice, flagged: false));
        Assert.False(decryptor.KeyRejected);
    }

    [Fact]
    public void RejectsWrongKey()
    {
        var vps = Nal([0x40, 0x01, 0x0c, 0x01, 0xff, 0xff], 30);

        using var h264 = new HikvisionNalDecryptor(Key, VideoCodec.H264);
        using var h265 = new HikvisionNalDecryptor(Key, VideoCodec.H265);

        Assert.Null(h264.Decrypt(EncryptH264(Nal([0x65], 200), "other-key"), flagged: true));
        Assert.Null(h265.Decrypt(EncryptH265(vps, "other-key"), flagged: true));
        Assert.True(h264.KeyRejected);
        Assert.True(h265.KeyRejected);
    }

    [Fact]
    public void PassesUnflaggedNalUnitsAfterFlaggedOne()
    {
        var slice = Nal([0x41], 64);
        var encrypted = EncryptH264(slice, Key);

        using var decryptor = new HikvisionNalDecryptor(Key, VideoCodec.H264);

        Assert.Equal(slice, decryptor.Decrypt(encrypted, flagged: true));
        Assert.Null(decryptor.Decrypt(encrypted, flagged: false));
    }

    private static byte[] Nal(byte[] start, int length)
    {
        var nal = new byte[length];

        for (var index = 0; index < length; index++)
        {
            nal[index] = (byte)(index * 7 + 3);
        }

        start.CopyTo(nal, 0);

        return nal;
    }

    // H.264: clear header, then the whole NAL unit with its first whole blocks (at most 4 KB) encrypted.
    private static byte[] EncryptH264(byte[] nal, string key)
    {
        return [nal[0], .. Encrypt(nal, key)];
    }

    // H.265: clear header, then the payload with its first whole blocks (at most 4 KB) encrypted.
    private static byte[] EncryptH265(byte[] nal, string key)
    {
        return [nal[0], nal[1], .. Encrypt(nal[2..], key)];
    }

    private static byte[] Encrypt(byte[] data, string key)
    {
        var keyBytes = new byte[16];

        Encoding.UTF8.GetBytes(key).CopyTo(keyBytes, 0);

        using var aes = Aes.Create();

        aes.Key = keyBytes;

        var count = Math.Min(data.Length / 16 * 16, 4096);
        var encrypted = aes.EncryptEcb(data.AsSpan(0, count), PaddingMode.None);

        return [.. encrypted, .. data.AsSpan(count)];
    }

    private static byte[] FlaggedRtp(ushort sequence, uint timestamp, byte[] nal)
    {
        var plain = RtpVideoAssemblerTests.Rtp(sequence, timestamp, true, nal);
        var header = plain.AsSpan(0, 12).ToArray();

        header[0] |= 0x10;

        // Header extension 0x4000 with one 32-bit word of data.
        return [.. header, 0x40, 0x00, 0x00, 0x01, 0, 0, 0, 0, .. nal];
    }
}
