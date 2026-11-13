namespace NovaVMS.App.Models;

/// <summary>
/// Summary of a settings import.
/// </summary>
public sealed record ImportResult
{
    public int DevicesAdded { get; init; }

    public int DevicesUpdated { get; init; }

    public int GroupsAdded { get; init; }

    public int GroupsUpdated { get; init; }

    public ImportResult(int devicesAdded, int devicesUpdated, int groupsAdded, int groupsUpdated)
    {
        DevicesAdded = devicesAdded;
        DevicesUpdated = devicesUpdated;
        GroupsAdded = groupsAdded;
        GroupsUpdated = groupsUpdated;
    }
}
