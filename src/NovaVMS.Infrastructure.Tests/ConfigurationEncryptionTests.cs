using System.Security.Cryptography;
using System.Text;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class ConfigurationEncryptionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-encryption-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "config.json");
    private readonly JsonConfigurationSerializer _serializer = new();

    private static AppConfiguration Configuration() => new()
    {
        Devices = [new Device
        {
            Name = "Secret camera", Host = "192.168.101.102", Password = "unique-password-marker",
            StreamEncryptionKey = "unique-stream-key-marker", Channels = [new Channel { IsFavorite = true }]
        }]
    };

    [Fact]
    public async Task SavedFileUsesCurrentUserDpapiAndRoundTripsAllSettings()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Store().SaveAsync(Configuration());
        var bytes = await File.ReadAllBytesAsync(FilePath);
        Assert.DoesNotContain("unique-password-marker", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("unique-stream-key-marker", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("192.168.101.102", Encoding.UTF8.GetString(bytes));
        var header = "NovaVMS.Settings.DPAPI.v1\0"u8.ToArray();
        Assert.True(bytes.AsSpan().StartsWith(header));
        var plaintext = ProtectedData.Unprotect(bytes[header.Length..], "NovaVMS.Configuration"u8.ToArray(), DataProtectionScope.CurrentUser);
        try { Assert.Contains("unique-password-marker", Encoding.UTF8.GetString(plaintext)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var restored = await Store().LoadAsync();
        var device = Assert.Single(restored.Devices);
        Assert.Equal("unique-password-marker", device.Password);
        Assert.Equal("unique-stream-key-marker", device.StreamEncryptionKey);
        Assert.True(Assert.Single(device.Channels).IsFavorite);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyJsonIsMigratedImmediatelyWithoutPlaintextBackup(bool withBom)
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_directory);
        await using (var stream = File.Create(FilePath))
        {
            if (withBom) await stream.WriteAsync(Encoding.UTF8.Preamble.ToArray());
            await _serializer.SerializeAsync(stream, Configuration());
        }
        Assert.Contains("unique-password-marker", await File.ReadAllTextAsync(FilePath));
        var loaded = await Store().LoadAsync();
        Assert.Equal("unique-password-marker", Assert.Single(loaded.Devices).Password);
        Assert.DoesNotContain("unique-password-marker", await File.ReadAllTextAsync(FilePath));
        Assert.Equal(loaded.Devices[0].Id, (await Store().LoadAsync()).Devices[0].Id);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CorruptedCiphertextCannotBeLoadedOrOverwrittenAndRecoveryIsPossible()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Store().SaveAsync(Configuration());
        var original = await File.ReadAllBytesAsync(FilePath);
        var corrupted = (byte[])original.Clone();
        corrupted[^1] ^= 0xff;
        await File.WriteAllBytesAsync(FilePath, corrupted);
        var store = Store();
        await Assert.ThrowsAsync<IOException>(() => store.LoadAsync());
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new AppConfiguration()));
        Assert.Equal(corrupted, await File.ReadAllBytesAsync(FilePath));
        await File.WriteAllBytesAsync(FilePath, original);
        await store.LoadAsync();
        await store.SaveAsync(Configuration());
        Assert.Single((await Store().LoadAsync()).Devices);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Devices\": [")]
    [InlineData("NovaVMS.Settings.DPAPI.v2")]
    public async Task InvalidOrUnknownFileIsPreserved(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content);
        var store = Store();
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Configuration()));
        Assert.Equal(content, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task SerializationFailureLeavesOriginalEncryptedFileUntouched()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Store().SaveAsync(Configuration());
        var original = await File.ReadAllBytesAsync(FilePath);
        var store = new JsonConfigurationStore(new FailingSerializer(), FilePath);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Configuration()));
        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task MissingFileLoadsDefaultsWithoutWriting()
    {
        Assert.Empty((await Store().LoadAsync()).Devices);
        Assert.False(File.Exists(FilePath));
    }

    private JsonConfigurationStore Store() => new(_serializer, FilePath);

    private sealed class FailingSerializer : IConfigurationSerializer
    {
        public async Task SerializeAsync(Stream stream, AppConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await stream.WriteAsync("unique-password-marker"u8.ToArray(), cancellationToken);
            throw new IOException("Simulated serialization failure.");
        }

        public Task<AppConfiguration> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NovaVMS-encryption-tests")) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(_directory);
        if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
