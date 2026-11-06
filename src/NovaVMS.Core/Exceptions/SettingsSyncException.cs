namespace NovaVMS.Core.Exceptions;

/// <summary>
/// Failure of an operation on the shared settings database.
/// </summary>
public sealed class SettingsSyncException : Exception
{
    public SettingsSyncFailure Failure { get; }

    public SettingsSyncException(SettingsSyncFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public SettingsSyncException(SettingsSyncFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }
}
