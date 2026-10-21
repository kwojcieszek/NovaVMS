namespace NovaVMS.Updater;

public sealed record UpdatePlan
{
    public required string InstallDirectory { get; init; }
    public required string PackagePath { get; init; }
    public required string PackageSha256 { get; init; }
    public required string Version { get; init; }
    public required string JobDirectory { get; init; }
    public string ExecutableName { get; init; } = "NovaVMS.exe";
    public string[] Arguments { get; init; } = [];
}
