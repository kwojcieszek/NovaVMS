using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Gives channels read from a device the identities of the channels already known, so views, groups and
/// favorites keep pointing at the same cameras. Channel numbers alone are not reliable: imports, ONVIF profile
/// order and the SDK fallback can number the same cameras differently.
/// </summary>
internal static class ChannelIdentity
{
    /// <summary>
    /// Matches each new channel to a known one, strongest evidence first: the same stream address or ONVIF profile,
    /// then the same name (when it is unique on both sides), then the same number. A matched channel takes over the
    /// known identity together with what the user set on it; unmatched channels keep their new identity.
    /// </summary>
    public static void Keep(IReadOnlyList<Channel> known, IReadOnlyList<Channel> current)
    {
        var unmatchedKnown = known.ToList();
        var unmatchedCurrent = current.ToList();

        MatchBy(unmatchedKnown, unmatchedCurrent, SameStream);
        var knownNames = CountNames(known);
        var currentNames = CountNames(current);
        MatchBy(unmatchedKnown, unmatchedCurrent, (old, channel) =>
            !string.IsNullOrWhiteSpace(channel.Name)
            && string.Equals(old.Name.Trim(), channel.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            && knownNames.GetValueOrDefault(channel.Name.Trim()) == 1
            && currentNames.GetValueOrDefault(channel.Name.Trim()) == 1);
        MatchBy(unmatchedKnown, unmatchedCurrent, (old, channel) => old.Number == channel.Number);
    }

    private static void MatchBy(List<Channel> known, List<Channel> current, Func<Channel, Channel, bool> isSame)
    {
        foreach (var channel in current.ToList())
        {
            var old = known.FirstOrDefault(candidate => isSame(candidate, channel));
            if (old is null)
            {
                continue;
            }

            channel.Id = old.Id;
            channel.IsFavorite = old.IsFavorite;
            // Prefer the encoder dimensions reported by the device; retain learned values as a fallback.
            channel.MainAspectRatio ??= old.MainAspectRatio;
            // The SDK fallback reports channels without names.
            if (string.IsNullOrWhiteSpace(channel.Name))
            {
                channel.Name = old.Name;
            }

            known.Remove(old);
            current.Remove(channel);
        }
    }

    private static bool SameStream(Channel old, Channel channel)
    {
        return (!string.IsNullOrEmpty(channel.ProfileToken) && channel.ProfileToken == old.ProfileToken)
            || (!string.IsNullOrEmpty(channel.MainStreamUri) && channel.MainStreamUri == old.MainStreamUri);
    }

    private static Dictionary<string, int> CountNames(IEnumerable<Channel> channels)
    {
        return channels.Where(channel => !string.IsNullOrWhiteSpace(channel.Name))
            .GroupBy(channel => channel.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }
}
