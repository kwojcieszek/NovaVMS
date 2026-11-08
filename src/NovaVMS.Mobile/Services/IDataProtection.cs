namespace NovaVMS.Mobile.Services;

/// <summary>Encrypts configuration with a key held by the platform secure storage.</summary>
public interface IDataProtection
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}
