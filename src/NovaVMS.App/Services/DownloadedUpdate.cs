namespace NovaVMS.App.Services;

internal sealed record DownloadedUpdate(ApplicationUpdate Update, string PackagePath, string Sha256);
