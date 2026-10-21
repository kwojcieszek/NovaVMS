namespace NovaVMS.Core.Models;

/// <summary>
/// Outcome of sending the settings: the new version, or the newest one when the content has not changed.
/// </summary>
public sealed record SettingsUploadResult(SettingsVersionInfo Version, bool Created);
