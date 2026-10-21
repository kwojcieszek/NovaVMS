namespace NovaVMS.Core.Exceptions;

/// <summary>
/// Reason a settings database operation failed.
/// </summary>
public enum SettingsSyncFailure
{
    ConnectionFailed,
    WrongEncryptionPassword,
    CorruptedData,
    UnsupportedSchema,
    VersionNotFound
}
