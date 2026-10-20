using System.Globalization;
using System.Text.Json.Serialization;

namespace NovaVMS.Core.Models;

/// <summary>
/// Video channel of a device (a camera or a recorder input).
/// </summary>
public sealed class Channel
{
    public int Number { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Main stream address reported by the device (ONVIF). Hikvision (ISAPI) devices build it from the channel number.
    /// </summary>
    public string? MainStreamUri { get; set; }

    /// <summary>
    /// Sub stream address reported by the device (ONVIF).
    /// </summary>
    public string? SubStreamUri { get; set; }

    /// <summary>
    /// Media profile used for PTZ commands (ONVIF).
    /// </summary>
    public string? ProfileToken { get; set; }

    /// <summary>
    /// Channel state read during the last synchronization (not persisted).
    /// </summary>
    [JsonIgnore]
    public bool Online { get; set; } = true;

    /// <summary>
    /// Name shown for channels without a name; "{0}" is the channel number. Set by the interface language.
    /// </summary>
    public static string DefaultNameFormat { get; set; } = "Camera {0}";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? string.Format(CultureInfo.CurrentCulture, DefaultNameFormat, Number)
        : Name;
}
