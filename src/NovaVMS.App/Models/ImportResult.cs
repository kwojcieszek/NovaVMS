namespace NovaVMS.App.Models;

/// <summary>
/// Summary of a settings import.
/// </summary>
public sealed record ImportResult(int DevicesAdded, int DevicesUpdated, int GroupsAdded, int GroupsUpdated);
