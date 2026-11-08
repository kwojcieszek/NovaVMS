using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using NovaVMS.Mobile.Services;
using CryptographicException = System.Security.Cryptography.CryptographicException;

namespace NovaVMS.Mobile.Android;

/// <summary>AES-GCM protection with a non-exportable key held by the Android Keystore.</summary>
public sealed class AndroidProtection : IDataProtection
{
    private const string Alias = "novavms.configuration.v1";
    private const string KeyStoreName = "AndroidKeyStore";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int NonceSize = 12;
    private const int TagSizeBits = 128;
    private const int MinimumEncryptedSize = 28;

    public byte[] Protect(byte[] data)
    {
        using var key = GetKey();
        using var cipher = Cipher.GetInstance(Transformation)!;

        cipher.Init(CipherMode.EncryptMode, key);

        var nonce = cipher.GetIV()!;

        if (nonce.Length != NonceSize)
        {
            throw new CryptographicException("Invalid nonce size.");
        }

        return [.. nonce, .. cipher.DoFinal(data)!];
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < MinimumEncryptedSize)
        {
            throw new CryptographicException("Invalid encrypted data.");
        }

        using var key = GetKey();
        using var cipher = Cipher.GetInstance(Transformation)!;
        using var parameters = new GCMParameterSpec(TagSizeBits, data[..NonceSize]);

        cipher.Init(CipherMode.DecryptMode, key, parameters);

        return cipher.DoFinal(data[NonceSize..])!;
    }

    private static IKey GetKey()
    {
        using var store = KeyStore.GetInstance(KeyStoreName)!;

        store.Load(null);

        if (store.GetKey(Alias, null) is { } key)
        {
            return key;
        }

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName)!;
        using var builder = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
        using var specification = builder.SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(256)!
            .Build();

        generator.Init(specification);

        return generator.GenerateKey()!;
    }
}
