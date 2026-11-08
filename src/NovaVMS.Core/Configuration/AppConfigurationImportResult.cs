namespace NovaVMS.Core.Configuration;

/// <summary>
/// Summary of a settings import.
/// </summary>
public sealed record AppConfigurationImportResult(int DevicesAdded, int DevicesUpdated, int GroupsAdded, int GroupsUpdated);
