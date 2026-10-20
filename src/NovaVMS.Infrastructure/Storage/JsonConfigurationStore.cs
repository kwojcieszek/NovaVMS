using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace NovaVMS.Infrastructure.Storage;

/// <summary>
/// Stores JSON encrypted for the current system account. Existing JSON is migrated on load.
/// Only encrypted data is written to disk, using an atomic replacement.
/// </summary>
public sealed class JsonConfigurationStore : IConfigurationStore
{
    private readonly IConfigurationSerializer _serializer;
    private readonly string _filePath;
    private readonly IConfigurationProtection _protection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _loadFailed;

    public JsonConfigurationStore(IConfigurationSerializer serializer)
        : this(serializer, AppDataPaths.ConfigurationFile)
    {
    }

    public JsonConfigurationStore(IConfigurationSerializer serializer, string filePath)
        : this(serializer, filePath, new ConfigurationProtection())
    {
    }

    public JsonConfigurationStore(IConfigurationSerializer serializer, string filePath, IConfigurationProtection protection)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(protection);

        _serializer = serializer;
        _filePath = filePath;
        _protection = protection;
    }

    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppConfiguration();
            }

            var stored = await File.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
            var encrypted = ConfigurationProtection.IsEncrypted(stored);
            var plaintext = encrypted
                ? await Task.Run(() => _protection.Unprotect(stored), cancellationToken).ConfigureAwait(false)
                : stored;
            try
            {
                // A valid legacy file must be an object; e.g. "null" must never replace settings.
                var offset = plaintext.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
                using var document = JsonDocument.Parse(plaintext.AsMemory(offset));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The configuration must contain a JSON object.");

                using var stream = new MemoryStream(plaintext, offset, plaintext.Length - offset, writable: false);
                var configuration = await _serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!encrypted)
                    await WriteEncryptedAsync(plaintext, cancellationToken).ConfigureAwait(false);
                _loadFailed = false;
                return configuration;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch
        {
            _loadFailed = true;
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loadFailed)
                throw new IOException("Saving is disabled because the configuration could not be loaded. Restore it and load it successfully before saving.");

            using var stream = new MemoryStream();
            try
            {
                await _serializer.SerializeAsync(stream, configuration, cancellationToken).ConfigureAwait(false);
                await WriteEncryptedAsync(stream.GetBuffer().AsMemory(0, checked((int)stream.Length)), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(stream.GetBuffer());
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task WriteEncryptedAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken)
    {
        // Native keychain calls can unlock a collection; keep them off the UI thread.
        var encrypted = await Task.Run(() => _protection.Protect(plaintext.Span), cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(encrypted, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
