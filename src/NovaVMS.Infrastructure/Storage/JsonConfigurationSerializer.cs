using System.Text.Json;
using System.Text.Json.Serialization;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Storage;

/// <summary>
/// Serializes the configuration as indented JSON with enums written as names.
/// </summary>
public sealed class JsonConfigurationSerializer : IConfigurationSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task SerializeAsync(Stream stream, AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(configuration);

        return JsonSerializer.SerializeAsync(stream, configuration, SerializerOptions, cancellationToken);
    }

    public async Task<AppConfiguration> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var configuration = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return configuration ?? new AppConfiguration();
    }
}
