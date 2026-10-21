namespace NovaVMS.Core.Models;

/// <summary>
/// Settings version stored in the database together with the computer it was sent from.
/// </summary>
public sealed record SettingsVersionInfo(
    long Id,
    DateTimeOffset CreatedAt,
    Guid InstanceId,
    string MachineName,
    string IpAddress,
    string UserName,
    string AppVersion,
    string ContentHash);
