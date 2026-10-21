using System.Security.Cryptography;
using System.Text;

namespace NovaVMS.Infrastructure.Sync;

/// <summary>
/// Encrypts settings with keys derived from the shared password (PBKDF2-SHA256, AES-256-GCM)
/// and computes keyed content hashes, so the database never sees the plaintext.
/// </summary>
internal sealed class SettingsPayloadCipher : IDisposable
{
    public const int MinimumIterations = 100_000;
    public const int MaximumIterations = 10_000_000;
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static ReadOnlySpan<byte> Header => "NovaVMS.Sync.AESGCM.v1\0"u8;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _hashKey;

    public SettingsPayloadCipher(string password, byte[] salt, int iterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentNullException.ThrowIfNull(salt);
        // The iteration count comes from the database; bound it so a modified value cannot weaken or stall derivation.
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, MinimumIterations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaximumIterations);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var keys = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, KeyLength * 2);
        try
        {
            _encryptionKey = keys[..KeyLength];
            _hashKey = keys[KeyLength..];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(keys);
        }
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var prefixLength = Header.Length + NonceLength + TagLength;
        var result = new byte[prefixLength + plaintext.Length];
        Header.CopyTo(result);
        var nonce = result.AsSpan(Header.Length, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_encryptionKey, TagLength);
        aes.Encrypt(nonce, plaintext, result.AsSpan(prefixLength), result.AsSpan(Header.Length + NonceLength, TagLength), Header);
        return result;
    }

    /// <summary>
    /// Decrypts data; returns null when it was encrypted with another password or has been modified.
    /// </summary>
    public byte[]? Decrypt(ReadOnlySpan<byte> data)
    {
        var prefixLength = Header.Length + NonceLength + TagLength;
        if (!data.StartsWith(Header) || data.Length < prefixLength)
        {
            return null;
        }

        var plaintext = new byte[data.Length - prefixLength];
        try
        {
            using var aes = new AesGcm(_encryptionKey, TagLength);
            aes.Decrypt(data.Slice(Header.Length, NonceLength), data[prefixLength..],
                data.Slice(Header.Length + NonceLength, TagLength), plaintext, Header);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
    }

    /// <summary>
    /// Keyed hash of the content, used to detect unchanged settings without revealing them.
    /// </summary>
    public string ComputeHash(ReadOnlySpan<byte> plaintext)
    {
        return Convert.ToHexString(HMACSHA256.HashData(_hashKey, plaintext));
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_encryptionKey);
        CryptographicOperations.ZeroMemory(_hashKey);
    }
}
