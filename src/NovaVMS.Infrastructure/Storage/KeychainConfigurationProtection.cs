using System.Security.Cryptography;

namespace NovaVMS.Infrastructure.Storage;

internal interface IConfigurationKeyStore
{
    byte[]? ReadKey(string id);
    void CreateKey(string id, byte[] key);
}

/// <summary>An authenticated envelope; its random key lives only in the system credential store.</summary>
internal sealed class KeychainConfigurationProtection(IConfigurationKeyStore keyStore) : IConfigurationProtection
{
    private static ReadOnlySpan<byte> Header => "NovaVMS.Settings.AESGCM.v1\0"u8;
    private const int IdLength = 16, NonceLength = 12, TagLength = 16, KeyLength = 32;
    private Guid? _keyId;

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var id = _keyId ?? Guid.NewGuid();
        var key = _keyId.HasValue ? ReadKey(id) : RandomNumberGenerator.GetBytes(KeyLength);
        try
        {
            // Each new configuration gets its own identifier. Concurrent first saves cannot replace
            // each other's credential; loading always looks up the identifier from that file.
            if (!_keyId.HasValue) keyStore.CreateKey(id.ToString("N"), key);
            var prefixLength = Header.Length + IdLength;
            var result = new byte[prefixLength + NonceLength + TagLength + plaintext.Length];
            Header.CopyTo(result);
            id.TryWriteBytes(result.AsSpan(Header.Length, IdLength));
            var nonce = result.AsSpan(prefixLength, NonceLength);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, result.AsSpan(prefixLength + NonceLength + TagLength),
                result.AsSpan(prefixLength + NonceLength, TagLength), result.AsSpan(0, prefixLength));
            _keyId = id;
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> data)
    {
        var prefixLength = Header.Length + IdLength;
        if (!data.StartsWith(Header) || data.Length < prefixLength + NonceLength + TagLength)
            throw new IOException("The configuration encryption format is unsupported or the file is truncated.");
        var id = new Guid(data.Slice(Header.Length, IdLength));
        var key = ReadKey(id);
        var plaintext = new byte[data.Length - prefixLength - NonceLength - TagLength];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(data.Slice(prefixLength, NonceLength), data[(prefixLength + NonceLength + TagLength)..],
                data.Slice(prefixLength + NonceLength, TagLength), plaintext, data[..prefixLength]);
            _keyId = id;
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new IOException("The configuration cannot be decrypted or has been modified. Restore the original file and system credential.", exception);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private byte[] ReadKey(Guid id)
    {
        var key = keyStore.ReadKey(id.ToString("N"));
        if (key is null) throw new IOException("The configuration key is missing from the system credential store. Restore the original credential or transfer settings using JSON export/import.");
        if (key.Length == KeyLength) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new IOException("The system credential contains an invalid configuration key.");
    }
}
