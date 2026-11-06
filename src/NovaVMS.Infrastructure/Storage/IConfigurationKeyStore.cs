namespace NovaVMS.Infrastructure.Storage;

internal interface IConfigurationKeyStore
{
    byte[]? ReadKey(string id);
    void CreateKey(string id, byte[] key);
}
