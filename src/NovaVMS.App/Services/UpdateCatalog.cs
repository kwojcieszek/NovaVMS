using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaVMS.App.Services;

public sealed record ApplicationUpdate(DateOnly Version, Uri PackageUri, string? Sha256)
{
    public string VersionText => Version.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
}

internal static partial class UpdateCatalog
{
    [GeneratedRegex(@"novavms_(\d{4})_(\d{2})_(\d{2})\.zip", RegexOptions.IgnoreCase)]
    private static partial Regex PackageName();

    public static DateOnly ParseVersion(string version) => DateOnly.ParseExact(version, "yyyy.MM.dd", CultureInfo.InvariantCulture);

    public static Uri ServerUri(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("Update server must be an absolute HTTP or HTTPS URL.");
        return uri;
    }

    public static async Task<ApplicationUpdate?> CheckAsync(HttpClient client, string address, string currentVersion, CancellationToken token)
    {
        var server = ServerUri(address);
        var current = ParseVersion(currentVersion);
        ApplicationUpdate? update;
        if (server.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            update = FromFilename(server);
            using var response = await client.GetAsync(server, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
        }
        else
        {
            var manifestUri = server.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? server : new Uri(server.AbsoluteUri.TrimEnd('/') + "/latest.json");
            using var response = await client.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.NotFound && !server.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // Plain static hosting can expose a directory listing without a manifest.
                var text = await ReadLimitedAsync(client, server, token);
                update = PackageName().Matches(text).Select(match => FromFilename(new Uri(new Uri(server.AbsoluteUri.TrimEnd('/') + "/"), match.Value)))
                    .OfType<ApplicationUpdate>().OrderByDescending(item => item.Version).FirstOrDefault();
            }
            else
            {
                response.EnsureSuccessStatusCode();
                await using var content = await response.Content.ReadAsStreamAsync(token);
                using var memory = new MemoryStream();
                await CopyLimitedAsync(content, memory, 1024 * 1024, null, token);
                using var json = JsonDocument.Parse(memory.ToArray());
                var root = json.RootElement;
                var version = ParseVersion(root.GetProperty("version").GetString()!);
                var file = root.TryGetProperty("file", out var filename) ? filename.GetString()! : $"novavms_{version:yyyy_MM_dd}.zip";
                var packageUri = new Uri(manifestUri, file);
                if (packageUri.Scheme != manifestUri.Scheme || packageUri.Host != manifestUri.Host || packageUri.Port != manifestUri.Port || packageUri.UserInfo.Length > 0)
                    throw new InvalidDataException("The update package must be hosted on the configured server.");
                var named = FromFilename(packageUri);
                if (named is null || named.Version != version) throw new InvalidDataException("The ZIP filename does not match the release version.");
                var hash = root.TryGetProperty("sha256", out var sha) ? sha.GetString() : null;
                if (hash is not null && (hash.Length != 64 || !hash.All(Uri.IsHexDigit))) throw new InvalidDataException("Invalid update SHA-256 checksum.");
                update = new ApplicationUpdate(version, packageUri, hash);
            }
        }
        return update?.Version > current ? update : null;
    }

    private static ApplicationUpdate? FromFilename(Uri uri)
    {
        var name = Path.GetFileName(uri.AbsolutePath);
        var match = PackageName().Match(name);
        if (!match.Success || match.Length != name.Length) return null;
        return DateOnly.TryParseExact($"{match.Groups[1]}.{match.Groups[2]}.{match.Groups[3]}", "yyyy.MM.dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? new ApplicationUpdate(date, uri, null) : null;
    }

    private static async Task<string> ReadLimitedAsync(HttpClient client, Uri uri, CancellationToken token)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        await CopyLimitedAsync(stream, memory, 4 * 1024 * 1024, null, token);
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    internal static async Task CopyLimitedAsync(Stream source, Stream destination, long maximum, Action<long>? progress, CancellationToken token)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            copied = checked(copied + count);
            if (copied > maximum) throw new InvalidDataException("The update download exceeds the size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
            progress?.Invoke(copied);
        }
    }
}
