using System.Security.Cryptography;
using System.Text;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class KeychainConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-keychain-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "config.json");
    private readonly MemoryKeyStore _keys = new();
    private readonly JsonConfigurationSerializer _serializer = new();

    [Fact]
    public void EnvelopeRoundTripsAcrossInstancesAndUsesFreshNoncesWithTheSameKey()
    {
        var plaintext = Encoding.UTF8.GetBytes("secret camera password");
        var first = new KeychainConfigurationProtection(_keys);
        var encrypted = first.Protect(plaintext);
        Assert.DoesNotContain("secret camera password", Encoding.UTF8.GetString(encrypted));
        var restarted = new KeychainConfigurationProtection(_keys);
        Assert.Equal(plaintext, restarted.Unprotect(encrypted));
        var next = restarted.Protect(plaintext);
        Assert.False(encrypted.AsSpan().SequenceEqual(next));
        Assert.Equal(plaintext, first.Unprotect(next));
        Assert.Single(_keys.Keys);
        Assert.Equal(32, _keys.Keys.Single().Value.Length);
    }

    [Fact]
    public void IndependentConfigurationsDoNotReplaceEachOthersKeys()
    {
        var first = new KeychainConfigurationProtection(_keys).Protect("first"u8);
        var second = new KeychainConfigurationProtection(_keys).Protect("second"u8);
        Assert.Equal(2, _keys.Keys.Count);
        var reader = new KeychainConfigurationProtection(_keys);
        Assert.Equal("first", Encoding.UTF8.GetString(reader.Unprotect(first)));
        Assert.Equal("second", Encoding.UTF8.GetString(reader.Unprotect(second)));
    }

    [Theory]
    [InlineData(0)] // format/version
    [InlineData(27)] // credential identifier
    [InlineData(43)] // nonce
    [InlineData(55)] // authentication tag
    [InlineData(71)] // ciphertext
    public void ModifiedEnvelopeIsRejected(int index)
    {
        var encrypted = new KeychainConfigurationProtection(_keys).Protect("secret camera password"u8);
        encrypted[index] ^= 0xff;
        Assert.Throws<IOException>(() => new KeychainConfigurationProtection(_keys).Unprotect(encrypted));
        Assert.Single(_keys.Keys);
    }

    [Fact]
    public void TruncatedEnvelopeIsRejected()
    {
        var encrypted = new KeychainConfigurationProtection(_keys).Protect("secret"u8);
        for (var length = 0; length < encrypted.Length; length++)
        {
            var truncated = encrypted[..length];
            Assert.Throws<IOException>(() => new KeychainConfigurationProtection(_keys).Unprotect(truncated));
        }
    }

    [Fact]
    public async Task LegacyMigrationAndLaterSavesUseTheSystemKeyWithoutPlaintextTemporaryFiles()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{\"Devices\":[{\"Password\":\"secret-marker\",\"Channels\":[{\"IsFavorite\":true}]}]}");
        var loaded = await Store().LoadAsync();
        Assert.True(loaded.Devices[0].Channels[0].IsFavorite);
        Assert.DoesNotContain("secret-marker", await File.ReadAllTextAsync(FilePath));
        var restarted = Store();
        var restored = await restarted.LoadAsync();
        Assert.Equal("secret-marker", restored.Devices[0].Password);
        await restarted.SaveAsync(restored);
        Assert.Single(_keys.Keys);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task MissingKeyDoesNotGenerateAReplacementOrOverwriteTheConfiguration()
    {
        await Store().SaveAsync(new AppConfiguration());
        var original = await File.ReadAllBytesAsync(FilePath);
        _keys.Keys.Clear();
        var store = Store();
        await Assert.ThrowsAsync<IOException>(() => store.LoadAsync());
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new AppConfiguration()));
        Assert.Empty(_keys.Keys);
        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
    }

    [Fact]
    public async Task UnavailableKeyringPreservesLegacyFileAndBlocksSaving()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{\"Devices\":[]}");
        var original = await File.ReadAllBytesAsync(FilePath);
        _keys.Unavailable = true;
        var store = Store();
        await Assert.ThrowsAsync<IOException>(() => store.LoadAsync());
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new AppConfiguration()));
        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task UnavailableKeyringPreservesPreviouslyEncryptedFile()
    {
        var store = Store();
        await store.SaveAsync(new AppConfiguration());
        var original = await File.ReadAllBytesAsync(FilePath);
        _keys.Unavailable = true;
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new AppConfiguration()));
        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
    }

    [Fact]
    public void InvalidStoredKeyIsRejected()
    {
        var encrypted = new KeychainConfigurationProtection(_keys).Protect("secret"u8);
        _keys.Keys[_keys.Keys.Single().Key] = new byte[16];
        Assert.Throws<IOException>(() => new KeychainConfigurationProtection(_keys).Unprotect(encrypted));
    }

    private JsonConfigurationStore Store() => new(_serializer, FilePath, new KeychainConfigurationProtection(_keys));

    private sealed class MemoryKeyStore : IConfigurationKeyStore
    {
        public Dictionary<string, byte[]> Keys { get; } = new();
        public bool Unavailable { get; set; }
        public byte[]? ReadKey(string id)
        {
            if (Unavailable) throw new IOException("Simulated unavailable keyring.");
            return Keys.TryGetValue(id, out var key) ? (byte[])key.Clone() : null;
        }
        public void CreateKey(string id, byte[] key)
        {
            if (Unavailable) throw new IOException("Simulated unavailable keyring.");
            Keys.Add(id, (byte[])key.Clone());
        }
    }

    public void Dispose()
    {
        foreach (var key in _keys.Keys.Values) CryptographicOperations.ZeroMemory(key);
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NovaVMS-keychain-tests")) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(_directory);
        if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
