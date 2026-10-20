namespace NovaVMS.Infrastructure.Storage;

public interface IConfigurationProtection
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> data);
}
