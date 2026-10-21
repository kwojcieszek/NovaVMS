using System.Globalization;
using System.Text;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Resources;

namespace NovaVMS.Infrastructure.Import;

/// <summary>
/// Reads a device list from CSV, e.g. a device list exported from another video management program
/// or prepared in a spreadsheet. Columns are recognised by common English and Polish header names;
/// the delimiter (comma, semicolon or tab) is detected automatically.
/// </summary>
public sealed class DeviceCsvReader
{
    /// <summary>
    /// Port of the Hikvision SDK protocol. Unless the row selects the "SDK" protocol, it is replaced with the HTTP (ISAPI) port.
    /// </summary>
    private const int SdkPort = 8000;
    private const int DefaultHttpPort = 80;
    private const int DefaultRtspPort = 554;
    private const string DefaultUsername = "admin";

    private static readonly char[] CandidateDelimiters = { ';', ',', '\t' };

    private static readonly Dictionary<string, string[]> ColumnAliases = new()
    {
        ["name"] = new[] { "name", "devicename", "alias", "nickname", "nazwa", "nazwaurzadzenia" },
        ["host"] = new[] { "ip", "ipaddress", "address", "host", "hostname", "ipdomain", "domain", "ipordomain", "deviceaddress", "adres", "adresip", "adresipdomena" },
        ["port"] = new[] { "port", "httpport", "deviceport", "serverport", "porthttp" },
        ["rtspport"] = new[] { "rtspport", "portrtsp" },
        ["user"] = new[] { "user", "username", "login", "account", "uzytkownik", "nazwauzytkownika" },
        ["password"] = new[] { "password", "pass", "pwd", "haslo" },
        ["https"] = new[] { "https", "usehttps" },
        ["protocol"] = new[] { "protocol", "protokol", "type" }
    };

    /// <summary>
    /// Header and example row of a CSV file that this reader understands.
    /// </summary>
    public static string Template { get; } =
        "Name;IP Address;Port;RTSP Port;User Name;Password" + Environment.NewLine +
        "Warehouse recorder;192.168.1.64;80;554;admin;" + Environment.NewLine;

    public async Task<DeviceCsvResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Parse(Decode(buffer.ToArray()));
    }

    /// <summary>
    /// Decodes UTF-8 (with or without BOM). Spreadsheet programs often save CSV in the system ANSI code page
    /// (e.g. Windows-1250 for Polish), so invalid UTF-8 falls back to that code page.
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var offset = bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;

        if (System.Text.Unicode.Utf8.IsValid(bytes.AsSpan(offset)))
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        return Encoding.GetEncoding(codePage).GetString(bytes);
    }

    public DeviceCsvResult Parse(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = SplitRows(text, delimiter)
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();

        if (rows.Count == 0)
        {
            throw new FormatException(ImportText.Get("FileEmpty"));
        }

        var columns = MapColumns(rows[0]);
        if (!columns.ContainsKey("host"))
        {
            throw new FormatException(ImportText.Get("NoHostColumn"));
        }

        var devices = new List<Device>();
        var warnings = new List<string>();
        var sdkPortReplaced = false;

        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowNumber = index + 1;
            var host = GetCell(row, columns, "host");
            if (string.IsNullOrWhiteSpace(host))
            {
                warnings.Add(ImportText.Format("RowNoHost", rowNumber));
                continue;
            }

            var device = new Device
            {
                Name = GetCell(row, columns, "name"),
                Host = host,
                HttpPort = DefaultHttpPort,
                RtspPort = ParsePort(GetCell(row, columns, "rtspport")) ?? DefaultRtspPort,
                Username = GetCell(row, columns, "user") is { Length: > 0 } user ? user : DefaultUsername,
                Password = GetCell(row, columns, "password"),
                UseHttps = ParseBoolean(GetCell(row, columns, "https")),
                Protocol = ParseProtocol(GetCell(row, columns, "protocol"))
            };

            var port = ParsePort(GetCell(row, columns, "port"));
            if (device.Protocol == DeviceProtocol.HikvisionSdk)
            {
                device.SdkPort = port ?? SdkPort;
            }
            else if (port == SdkPort)
            {
                sdkPortReplaced = true;
            }
            else if (port is int httpPort)
            {
                device.HttpPort = httpPort;
            }

            if (string.IsNullOrEmpty(device.Password))
            {
                warnings.Add(ImportText.Format("RowNoPassword", rowNumber, host));
            }

            devices.Add(device);
        }

        if (sdkPortReplaced)
        {
            warnings.Insert(0, ImportText.Format("SdkPortReplaced", SdkPort, DefaultHttpPort));
        }

        return new DeviceCsvResult(devices, warnings);
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        return CandidateDelimiters
            .OrderByDescending(candidate => firstLine.Count(character => character == candidate))
            .First();
    }

    /// <summary>
    /// Splits CSV text into rows and cells, honouring quoted cells with embedded delimiters, quotes and line breaks.
    /// </summary>
    private static List<List<string>> SplitRows(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    cell.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == delimiter)
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else if (character == '\n')
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else if (character != '\r')
            {
                cell.Append(character);
            }
        }

        row.Add(cell.ToString().Trim());
        rows.Add(row);
        return rows;
    }

    private static Dictionary<string, int> MapColumns(IReadOnlyList<string> header)
    {
        var columns = new Dictionary<string, int>();
        for (var index = 0; index < header.Count; index++)
        {
            var normalized = NormalizeHeader(header[index]);
            foreach (var (column, aliases) in ColumnAliases)
            {
                if (!columns.ContainsKey(column) && aliases.Contains(normalized))
                {
                    columns[column] = index;
                }
            }
        }

        return columns;
    }

    /// <summary>
    /// Lower-cases the header and removes diacritics, spaces and punctuation ("Adres IP/Domena" becomes "adresipdomena").
    /// </summary>
    private static string NormalizeHeader(string header)
    {
        var decomposed = header.Trim().ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in decomposed)
        {
            if (char.IsLetterOrDigit(character) && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string GetCell(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string column)
    {
        if (!columns.TryGetValue(column, out var index) || index >= row.Count)
        {
            return string.Empty;
        }

        return row[index].Trim();
    }

    private static int? ParsePort(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535)
        {
            return port;
        }

        return null;
    }

    private static DeviceProtocol ParseProtocol(string value)
    {
        if (value.Contains("onvif", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceProtocol.Onvif;
        }

        return value.Contains("sdk", StringComparison.OrdinalIgnoreCase)
            ? DeviceProtocol.HikvisionSdk
            : DeviceProtocol.Isapi;
    }

    private static bool ParseBoolean(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "1" or "true" or "yes" or "tak";
    }
}
