using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace NovaVMS.Updater;

public static class UpdatePackage
{
    private const long MaximumExpandedBytes = 8L * 1024 * 1024 * 1024;

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static IReadOnlyList<(ZipArchiveEntry Entry, string Name)> Validate(ZipArchive archive)
    {
        var files = archive.Entries.Where(entry => !entry.FullName.EndsWith('/') && !entry.FullName.EndsWith('\\')).ToList();
        if (files.Count is 0 or > 30000) throw new InvalidDataException("Invalid update package file count.");
        var names = files.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
        var prefix = "";
        if (!names.Contains("NovaVMS.exe", StringComparer.OrdinalIgnoreCase))
        {
            var folders = names.Select(name => name.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (folders.Count == 1) prefix = folders[0] + "/";
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(ZipArchiveEntry, string)>();
        long expanded = 0;
        for (var index = 0; index < files.Count; index++)
        {
            var entry = files[index];
            var name = names[index];
            if (prefix.Length > 0) name = name[prefix.Length..];
            var parts = name.Split('/');
            if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains(':')
                || part.EndsWith(' ') || part.EndsWith('.') || part.IndexOfAny(['<', '>', '"', '|', '?', '*', '\0']) >= 0)
                || !seen.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Unsafe or duplicate path in update package.");
            if (parts[0].Equals("logs", StringComparison.OrdinalIgnoreCase)
                || parts.Any(part => part.Equals("config.json", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("sync.json", StringComparison.OrdinalIgnoreCase)
                    || part.Equals(".novavms-update.lock", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("An update package must not contain user settings or update locks.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes) throw new InvalidDataException("The expanded update package is too large.");
            result.Add((entry, name));
        }
        foreach (var required in new[] { "NovaVMS.exe", "NovaVMS.dll", "NovaVMS.deps.json", "NovaVMS.runtimeconfig.json",
            "updater/NovaVMS.Updater.exe", "updater/NovaVMS.Updater.dll", "updater/NovaVMS.Updater.deps.json", "updater/NovaVMS.Updater.runtimeconfig.json" })
            if (!seen.Contains(required)) throw new InvalidDataException($"The update package is missing {required}.");
        return result;
    }

    public static void Extract(string package, string destination, string version)
    {
        using var archive = ZipFile.OpenRead(package);
        var entries = Validate(archive);
        Directory.CreateDirectory(destination);
        foreach (var (entry, name) in entries)
        {
            var target = SafePath(destination, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
        if (!DateOnly.TryParseExact(version, "yyyy.MM.dd", out var date)) throw new InvalidDataException("Invalid release version.");
        var assembly = AssemblyName.GetAssemblyName(Path.Combine(destination, "NovaVMS.dll")).Version;
        if (assembly is null || assembly.Major != date.Year || assembly.Minor != date.Month || assembly.Build != date.Day)
            throw new InvalidDataException("The package version does not match the advertised release.");
    }

    public static string SafePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Update path escapes the target directory.");
        for (var parent = Path.GetDirectoryName(fullPath); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Update paths must not pass through symbolic links.");
            if (string.Equals(parent + Path.DirectorySeparatorChar, fullRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Update targets must not be symbolic links.");
        return fullPath;
    }
}
