using System.Security.Cryptography;

namespace NovaVMS.Infrastructure.Storage;

internal sealed class ConfigurationProtection : IConfigurationProtection
{
    private static ReadOnlySpan<byte> Header => "NovaVMS.Settings.DPAPI.v1\0"u8;
    private static ReadOnlySpan<byte> Purpose => "NovaVMS.Configuration"u8;
    private readonly IConfigurationProtection? _keychainProtection = OperatingSystem.IsMacOS()
        ? new KeychainConfigurationProtection(new MacConfigurationKeyStore())
        : OperatingSystem.IsLinux() ? new KeychainConfigurationProtection(new LinuxConfigurationKeyStore()) : null;

    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.StartsWith("NovaVMS.Settings."u8);

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (_keychainProtection is not null) return _keychainProtection.Protect(plaintext);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Account-bound configuration encryption requires Windows DPAPI.");
        var cleartext = plaintext.ToArray();
        byte[] ciphertext;
        try
        {
            ciphertext = ProtectedData.Protect(cleartext, Purpose.ToArray(), DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
        }
        var result = new byte[Header.Length + ciphertext.Length];
        Header.CopyTo(result);
        ciphertext.CopyTo(result, Header.Length);
        return result;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> data)
    {
        if (!data.StartsWith(Header))
        {
            if (_keychainProtection is not null) return _keychainProtection.Unprotect(data);
            throw new IOException("This configuration encryption format is not supported on this system. Transfer settings using JSON export/import.");
        }
        if (!OperatingSystem.IsWindows())
            throw new IOException("This configuration requires the original Windows account. Transfer settings using JSON export/import.");
        try
        {
            return ProtectedData.Unprotect(data[Header.Length..].ToArray(), Purpose.ToArray(), DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException exception)
        {
            throw new IOException("The configuration cannot be decrypted. Open it with the original Windows account or restore a backup.", exception);
        }
    }

}
