using System.Globalization;

namespace NovaVMS.App.Services;

public sealed record ApplicationUpdate(DateOnly Version, Uri PackageUri, string? Sha256)
{
    public string VersionText => Version.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
}
