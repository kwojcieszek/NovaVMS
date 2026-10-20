namespace NovaVMS.Core.Models;

/// <summary>
/// Stored position of a PTZ camera.
/// </summary>
public sealed class PtzPreset
{
    public int Id { get; init; }

    /// <summary>
    /// Identifier used by the device protocol (ISAPI uses the number, ONVIF uses a token).
    /// </summary>
    public string Token { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{Id}: {Name}";
}
