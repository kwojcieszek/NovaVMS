namespace NovaVMS.App.Services;

internal sealed record DownloadedUpdate
{
    public ApplicationUpdate Update { get; init; }

    public string PackagePath { get; init; }

    public string Sha256 { get; init; }

    public DownloadedUpdate(ApplicationUpdate update, string packagePath, string sha256)
    {
        Update = update;
        PackagePath = packagePath;
        Sha256 = sha256;
    }
}
