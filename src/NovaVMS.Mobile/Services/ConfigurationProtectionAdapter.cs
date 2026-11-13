using System.Security.Cryptography;
using NovaVMS.Infrastructure.Storage;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Lets the shared configuration stores encrypt with the platform secure storage (Android Keystore, iOS Keychain).
/// The header marks the file as encrypted, as the desktop protections do.
/// </summary>
public sealed class ConfigurationProtectionAdapter : IConfigurationProtection
{
    private readonly IDataProtection _protection;

    private static ReadOnlySpan<byte> Header => "NovaVMS.Settings.Mobile.v1\0"u8;

    public ConfigurationProtectionAdapter(IDataProtection protection)
    {
        _protection = protection;
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var encrypted = _protection.Protect(plaintext.ToArray());

        return [.. Header, .. encrypted];
    }

    public byte[] Unprotect(ReadOnlySpan<byte> data)
    {
        if (!data.StartsWith(Header))
        {
            throw new CryptographicException("The configuration was not encrypted on this device.");
        }

        return _protection.Unprotect(data[Header.Length..].ToArray());
    }
}
