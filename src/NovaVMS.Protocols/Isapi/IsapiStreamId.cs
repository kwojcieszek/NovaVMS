using NovaVMS.Core.Models;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// ISAPI stream numbering: channel × 100 + 1 (main) or + 2 (sub), e.g. 101, 102, 201.
/// </summary>
internal static class IsapiStreamId
{
    public const int Multiplier = 100;
    public const int MainSuffix = 1;
    public const int SubSuffix = 2;

    public static int Build(int channelNumber, StreamType streamType)
    {
        var suffix = streamType == StreamType.Main ? MainSuffix : SubSuffix;
        return channelNumber * Multiplier + suffix;
    }

    public static int Track(int channelNumber)
    {
        return Build(channelNumber, StreamType.Main);
    }
}
