using System.Globalization;

namespace NovaVMS.Core.Models;

/// <summary>
/// Devices without configured channel names report the stream number as the name (e.g. "101", "201").
/// Such names - and the plain channel number stored by earlier versions - are cleared, so the channel
/// is shown under its default name (<see cref="Channel.DefaultNameFormat"/>); real names are kept unchanged.
/// </summary>
public static class ChannelNameNormalizer
{
    private static readonly string[] StreamSuffixes = { "", "01", "02" };

    public static string Normalize(string name, int channelNumber)
    {
        var trimmed = name.Trim();
        var number = channelNumber.ToString(CultureInfo.InvariantCulture);

        foreach (var suffix in StreamSuffixes)
        {
            if (string.Equals(trimmed, number + suffix, StringComparison.Ordinal))
            {
                return string.Empty;
            }
        }

        return trimmed;
    }
}
