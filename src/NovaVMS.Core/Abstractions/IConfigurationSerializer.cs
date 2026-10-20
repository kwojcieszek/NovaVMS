using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// Converts the configuration to and from its file format (used for storage, export and import).
/// </summary>
public interface IConfigurationSerializer
{
    Task SerializeAsync(Stream stream, AppConfiguration configuration, CancellationToken cancellationToken = default);

    Task<AppConfiguration> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default);
}
