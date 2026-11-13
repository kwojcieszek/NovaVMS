using System.Globalization;

namespace NovaVMS.App.Services;

public sealed record ApplicationUpdate
{
    public DateOnly Version { get; init; }

    public Uri PackageUri { get; init; }

    public string? Sha256 { get; init; }

    public string VersionText => Version.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);

    public ApplicationUpdate(DateOnly version, Uri packageUri, string? sha256)
    {
        Version = version;
        PackageUri = packageUri;
        Sha256 = sha256;
    }
}
