using System.Text.Json;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Storage;

/// <summary>
/// Stores the settings of this computer as plain JSON in their own file. They contain no secrets.
/// A missing or unreadable file yields defaults, so a damaged file never prevents the application from starting.
/// </summary>
public sealed class LocalSettingsStore : ILocalSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LocalSettingsStore()
        : this(AppDataPaths.LocalSettingsFile)
    {
    }

    public LocalSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public LocalSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new LocalSettings();
            }

            var settings = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllBytes(_filePath), SerializerOptions) ?? new LocalSettings();
            settings.AutoLoginUserName ??= string.Empty;
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LocalSettings();
        }
    }

    public async Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
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
