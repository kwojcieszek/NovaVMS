using System.Security.Cryptography;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.Tests;

/// <summary>In-memory AES-GCM protection with an optional write failure.</summary>
internal sealed class TestProtection : IDataProtection
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = NonceSize + TagSize;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public bool FailWrites { get; set; }

    public byte[] Protect(byte[] data)
    {
        if (FailWrites)
        {
            throw new CryptographicException();
        }

        using var aes = new AesGcm(_key, TagSize);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[data.Length];

        aes.Encrypt(nonce, data, ciphertext, tag);

        return [.. nonce, .. tag, .. ciphertext];
    }

    public byte[] Unprotect(byte[] data)
    {
        using var aes = new AesGcm(_key, TagSize);

        var plain = new byte[data.Length - HeaderSize];

        aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(HeaderSize), data.AsSpan(NonceSize, TagSize), plain);

        return plain;
    }
}
