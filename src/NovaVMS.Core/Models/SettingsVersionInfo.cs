namespace NovaVMS.Core.Models;

/// <summary>
/// Settings version stored in the database together with the computer it was sent from.
/// </summary>
public sealed record SettingsVersionInfo
{
    public long Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid InstanceId { get; init; }

    public string MachineName { get; init; }

    public string IpAddress { get; init; }

    public string UserName { get; init; }

    public string AppVersion { get; init; }

    public string ContentHash { get; init; }

    public SettingsVersionInfo(
        long id,
        DateTimeOffset createdAt,
        Guid instanceId,
        string machineName,
        string ipAddress,
        string userName,
        string appVersion,
        string contentHash)
    {
        Id = id;
        CreatedAt = createdAt;
        InstanceId = instanceId;
        MachineName = machineName;
        IpAddress = ipAddress;
        UserName = userName;
        AppVersion = appVersion;
        ContentHash = contentHash;
    }
}
