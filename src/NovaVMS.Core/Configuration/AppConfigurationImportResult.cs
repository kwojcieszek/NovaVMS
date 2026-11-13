namespace NovaVMS.Core.Configuration;

/// <summary>
/// Summary of a settings import.
/// </summary>
public sealed record AppConfigurationImportResult
{
    public int DevicesAdded { get; init; }

    public int DevicesUpdated { get; init; }

    public int GroupsAdded { get; init; }

    public int GroupsUpdated { get; init; }

    public AppConfigurationImportResult(int devicesAdded, int devicesUpdated, int groupsAdded, int groupsUpdated)
    {
        DevicesAdded = devicesAdded;
        DevicesUpdated = devicesUpdated;
        GroupsAdded = groupsAdded;
        GroupsUpdated = groupsUpdated;
    }
}
