namespace NovaVMS.Core.Models;

/// <summary>
/// Outcome of sending the settings: the new version, or the newest one when the content has not changed.
/// </summary>
public sealed record SettingsUploadResult
{
    public SettingsVersionInfo Version { get; init; }

    public bool Created { get; init; }

    public SettingsUploadResult(SettingsVersionInfo version, bool created)
    {
        Version = version;
        Created = created;
    }
}
