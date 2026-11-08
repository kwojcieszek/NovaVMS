using System.Security.Cryptography;
using NovaVMS.Infrastructure.Storage;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Lets the shared configuration stores encrypt with the platform secure storage (Android Keystore, iOS Keychain).
/// The header marks the file as encrypted, as the desktop protections do.
/// </summary>
public sealed class ConfigurationProtectionAdapter(IDataProtection protection) : IConfigurationProtection
{
    private static ReadOnlySpan<byte> Header => "NovaVMS.Settings.Mobile.v1\0"u8;

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var encrypted = protection.Protect(plaintext.ToArray());

        return [.. Header, .. encrypted];
    }

    public byte[] Unprotect(ReadOnlySpan<byte> data)
    {
        if (!data.StartsWith(Header))
        {
            throw new CryptographicException("The configuration was not encrypted on this device.");
        }

        return protection.Unprotect(data[Header.Length..].ToArray());
    }
}
