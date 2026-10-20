using System.Globalization;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// Time formatting for ISAPI and RTSP.
/// Most Hikvision firmware treats timestamps as the device's local time despite the "Z" suffix,
/// so values are not converted to UTC.
/// </summary>
internal static class IsapiTime
{
    private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string CompactFormat = "yyyyMMdd'T'HHmmss'Z'";

    public static string ToIso(DateTime value)
    {
        return value.ToString(IsoFormat, CultureInfo.InvariantCulture);
    }

    public static string ToCompact(DateTime value)
    {
        return value.ToString(CompactFormat, CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string text, out DateTime value)
    {
        if (DateTime.TryParseExact(text, IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
        {
            value = offset.DateTime;
            return true;
        }

        value = default;
        return false;
    }
}
