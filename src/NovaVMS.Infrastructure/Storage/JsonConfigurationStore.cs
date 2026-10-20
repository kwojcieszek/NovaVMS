using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Storage;

/// <summary>
/// Stores the configuration in a JSON file in the user's data directory.
/// Writes go to a temporary file first so a crash never leaves a truncated configuration.
/// </summary>
public sealed class JsonConfigurationStore : IConfigurationStore
{
    private readonly IConfigurationSerializer _serializer;
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonConfigurationStore(IConfigurationSerializer serializer)
        : this(serializer, AppDataPaths.ConfigurationFile)
    {
    }

    public JsonConfigurationStore(IConfigurationSerializer serializer, string filePath)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _serializer = serializer;
        _filePath = filePath;
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

            await using var stream = File.OpenRead(_filePath);
            return await _serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
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
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _filePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await _serializer.SerializeAsync(stream, configuration, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
