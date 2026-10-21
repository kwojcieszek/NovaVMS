namespace NovaVMS.Core.Models;

/// <summary>
/// What a user may do. Viewing live video is always allowed to a logged-in user.
/// </summary>
[Flags]
public enum UserPermissions
{
    None = 0,

    /// <summary>General settings page, including the settings of this computer.</summary>
    GeneralSettings = 1,

    /// <summary>Adding, editing, importing and removing devices.</summary>
    Devices = 2,

    /// <summary>Creating and editing camera groups.</summary>
    Groups = 4,

    /// <summary>Settings database connection and versions.</summary>
    SettingsSync = 8,

    /// <summary>Adding, editing and removing users and their permissions.</summary>
    Users = 16,

    /// <summary>Creating, renaming, deleting and changing saved views.</summary>
    Views = 32,

    /// <summary>Adding cameras to and removing them from the favorites.</summary>
    Favorites = 64,

    /// <summary>Recordings: playback and export.</summary>
    Playback = 128,

    /// <summary>Pan, tilt and zoom control.</summary>
    Ptz = 256,

    All = GeneralSettings | Devices | Groups | SettingsSync | Users | Views | Favorites | Playback | Ptz,

    /// <summary>Any page of the settings window.</summary>
    AnySettings = GeneralSettings | Devices | Groups | SettingsSync | Users
}
