using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Storage;

/// <summary>
/// Stores the settings database connection in its own file, encrypted for the current system account
/// like the main configuration. A missing file yields defaults with a new installation identifier.
/// </summary>
public sealed class SettingsSyncOptionsStore : ISettingsSyncOptionsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly IConfigurationProtection _protection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingsSyncOptionsStore()
        : this(AppDataPaths.SyncOptionsFile, new ConfigurationProtection())
    {
    }

    public SettingsSyncOptionsStore(string filePath, IConfigurationProtection protection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(protection);

        _filePath = filePath;
        _protection = protection;
    }

    public async Task<SettingsSyncOptions> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new SettingsSyncOptions();
            }

            var stored = await File.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
            var plaintext = await Task.Run(() => _protection.Unprotect(stored), cancellationToken).ConfigureAwait(false);
            try
            {
                var options = JsonSerializer.Deserialize<SettingsSyncOptions>(plaintext, SerializerOptions) ?? new SettingsSyncOptions();
                options.Host ??= string.Empty;
                options.Database ??= string.Empty;
                options.UserName ??= string.Empty;
                options.Password ??= string.Empty;
                options.EncryptionPassword ??= string.Empty;
                if (options.InstanceId == Guid.Empty)
                {
                    options.InstanceId = Guid.NewGuid();
                }

                return options;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(SettingsSyncOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(options, SerializerOptions);
            byte[] encrypted;
            try
            {
                encrypted = await Task.Run(() => _protection.Protect(plaintext), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
