using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NovaVMS.App.Localization;
using NovaVMS.Updater;

namespace NovaVMS.App.Services;

internal sealed record DownloadedUpdate(ApplicationUpdate Update, string PackagePath, string Sha256);

/// <summary>Retains verified downloads for later installation, including after a restart.</summary>
internal sealed class UpdateDownloadCache(HttpClient http, string directory)
{
    private string PackagePath(ApplicationUpdate update)
    {
        var identity = update.PackageUri.AbsoluteUri + "\n" + update.Sha256;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(directory, key + ".zip");
    }

    internal async Task<DownloadedUpdate?> TryGetAsync(ApplicationUpdate update, CancellationToken token)
    {
        var package = PackagePath(update);
        if (!File.Exists(package) || !File.Exists(package + ".sha256")) return null;
        try
        {
            var storedHash = (await File.ReadAllTextAsync(package + ".sha256", token)).Trim();
            var hash = await VerifyAsync(update, package, token);
            return hash.Equals(storedHash, StringComparison.OrdinalIgnoreCase) ? new(update, package, hash) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException) { return null; }
    }

    internal async Task<DownloadedUpdate> GetAsync(ApplicationUpdate update, IProgress<double>? progress, CancellationToken token)
    {
        if (await TryGetAsync(update, token) is { } cached) return cached;
        Directory.CreateDirectory(directory);
        var package = PackagePath(update);
        var partial = package + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var response = await http.GetAsync(update.PackageUri, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                var size = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await UpdateCatalog.CopyLimitedAsync(input, output, 4L * 1024 * 1024 * 1024,
                    downloaded => { if (size is > 0) progress?.Report(Math.Min(99, downloaded * 100d / size.Value)); }, token);
            }
            var hash = await VerifyAsync(update, partial, token);
            token.ThrowIfCancellationRequested();
            File.Move(partial, package, overwrite: true);
            await File.WriteAllTextAsync(package + ".sha256", hash, token);
            progress?.Report(100);
            return new(update, package, hash);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private static Task<string> VerifyAsync(ApplicationUpdate update, string package, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var hash = UpdatePackage.Sha256(package);
        if (update.Sha256 is not null && !hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Localizer.Text("Updates_ChecksumFailed"));
        using var archive = ZipFile.OpenRead(package);
        UpdatePackage.Validate(archive);
        token.ThrowIfCancellationRequested();
        return hash;
    }, token);
}
