using System.Security.Cryptography;
using Foundation;
using NovaVMS.Mobile.Services;
using Security;

namespace NovaVMS.Mobile.iOS;

/// <summary>AES-GCM protection with a device-only key stored in the iOS Keychain.</summary>
public sealed class KeychainProtection : IDataProtection
{
    private const string KeychainService = "com.novavms.mobile";
    private const string KeychainAccount = "configuration.v1";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = NonceSize + TagSize;

    public byte[] Protect(byte[] data)
    {
        var key = GetKey();

        try
        {
            using var aes = new AesGcm(key, TagSize);

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var encrypted = new byte[data.Length];

            aes.Encrypt(nonce, data, encrypted, tag);

            return [.. nonce, .. tag, .. encrypted];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new CryptographicException("Invalid encrypted data.");
        }

        var key = GetKey();

        try
        {
            using var aes = new AesGcm(key, TagSize);

            var plain = new byte[data.Length - HeaderSize];

            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(HeaderSize), data.AsSpan(NonceSize, TagSize), plain);

            return plain;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] GetKey()
    {
        using var query = new SecRecord(SecKind.GenericPassword)
        {
            Service = KeychainService,
            Account = KeychainAccount
        };
        using var found = SecKeyChain.QueryAsData(query, false, out var status);

        if (status == SecStatusCode.Success && found is not null)
        {
            return found.ToArray();
        }

        if (status != SecStatusCode.ItemNotFound)
        {
            throw new CryptographicException("Keychain is unavailable.");
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        using var data = NSData.FromArray(key);

        query.ValueData = data;
        query.Accessible = SecAccessible.WhenUnlockedThisDeviceOnly;

        if (SecKeyChain.Add(query) != SecStatusCode.Success)
        {
            throw new CryptographicException("Cannot create configuration key.");
        }

        return key;
    }
}
