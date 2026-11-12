using System.Text;
using System.Text.RegularExpressions;
using NovaVMS.Presentation.Models;

namespace NovaVMS.Presentation.Services;

/// <summary>Reads the exception log files (errors-yyyyMMdd.log) written by the desktop and the mobile application.</summary>
public static partial class ApplicationLogFile
{
    private const int PreviewBytes = 128 * 1024;

    /// <summary>Splits the log text into entries, newest first; text before the first entry becomes one entry.</summary>
    public static IReadOnlyList<ApplicationLogEntry> ParseEntries(string text)
    {
        var rows = new List<ApplicationLogEntry>();
        var headers = EntryHeader().Matches(text);
        var prefix = text[..(headers.Count == 0 ? text.Length : headers[0].Index)].Trim();

        if (prefix.Length > 0)
        {
            rows.Add(new ApplicationLogEntry("", "", "", "", prefix));
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            var details = text[(header.Index + header.Length)..end].TrimEnd().TrimStart('\r', '\n');

            rows.Add(new ApplicationLogEntry(header.Groups[1].Value, header.Groups[2].Value,
                header.Groups[3].Value, header.Groups[4].Value, details));
        }

        rows.Reverse();

        return rows;
    }

    /// <summary>Reads the end of the newest log file in <paramref name="directories"/>; the path is empty without one.</summary>
    public static (string Path, string Text, bool Truncated) ReadLatest(params string[] directories)
    {
        var latest = directories.Distinct().Where(Directory.Exists)
            .SelectMany(directory => new DirectoryInfo(directory).EnumerateFiles("errors-????????.log"))
            .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();

        if (latest is null)
        {
            return (string.Empty, string.Empty, false);
        }

        using var stream = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var count = (int)Math.Min(length, PreviewBytes);
        stream.Position = length - count;
        var bytes = new byte[count];

        stream.ReadExactly(bytes);

        var start = 0;

        if (length > count)
        {
            // The tail can start in the middle of a UTF-8 character.
            while (start < count && (bytes[start] & 0xC0) == 0x80)
            {
                start++;
            }
        }

        var text = Encoding.UTF8.GetString(bytes, start, count - start);

        return (latest.FullName, text, length > count);
    }

    [GeneratedRegex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2})\] \[PID (\d+), thread (\d+)\] ([^\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex EntryHeader();
}
