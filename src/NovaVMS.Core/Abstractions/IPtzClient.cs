using NovaVMS.Core.Models;

namespace NovaVMS.Core.Abstractions;

/// <summary>
/// PTZ camera control.
/// </summary>
public interface IPtzClient
{
    /// <summary>
    /// Continuous movement; values range from -100 to 100.
    /// </summary>
    Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default);

    Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default);

    Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default);
}
