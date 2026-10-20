using System.Text.RegularExpressions;

namespace NovaVMS.Protocols.HcNetSdk;

internal sealed record HikvisionRtspDescription(byte[] Header, Uri Track, int PayloadType, string Codec, IReadOnlyList<byte[]> Parameters)
{
    internal static HikvisionRtspDescription Parse(string sdp, Uri baseUri)
    {
        var headerMatch = Regex.Match(sdp, @"a=Media_header:MEDIAINFO=([0-9a-f]+)", RegexOptions.IgnoreCase);
        if (!headerMatch.Success || headerMatch.Groups[1].Length is < 80 or > 8192 || headerMatch.Groups[1].Length % 2 != 0)
            throw new IOException("RTSP: Hikvision MEDIAINFO header is missing or invalid.");
        var header = Convert.FromHexString(headerMatch.Groups[1].Value);
        if (!header.AsSpan(0, 4).SequenceEqual("IMKH"u8)) throw new IOException("RTSP: invalid Hikvision media header.");
        var lines = sdp.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var video = false;
        var selected = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("m="))
            {
                if (video) break;
                video = line.StartsWith("m=video ", StringComparison.OrdinalIgnoreCase);
            }
            if (video) selected.Add(line.Trim());
        }
        var section = string.Join("\n", selected);
        if (selected.Count == 0 || selected[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2) != "RTP/AVP")
            throw new IOException("RTSP: secure RTP requires a different transport.");
        var map = Regex.Match(section, @"a=rtpmap:(\d+) (H264|H265|HEVC|MP2P|PS)/90000", RegexOptions.IgnoreCase);
        var control = selected.FirstOrDefault(line => line.StartsWith("a=control:", StringComparison.OrdinalIgnoreCase))?[10..];
        if (!map.Success || string.IsNullOrEmpty(control) || control == "*" || int.Parse(map.Groups[1].Value) > 127)
            throw new IOException("RTSP: unsupported video description.");
        if (Regex.IsMatch(section, @"(?:packetization-mode=2|sprop-max-don-diff=[1-9])", RegexOptions.IgnoreCase))
            throw new IOException("RTSP: interleaved decoding-order packetization is unsupported.");
        var trackBase = new UriBuilder(baseUri) { Query = "", Fragment = "", Path = baseUri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
        var track = new Uri(trackBase, control);
        EnsureSameServer(baseUri, track);
        var parameters = new List<byte[]>();
        foreach (var attribute in new[] { "sprop-parameter-sets", "sprop-vps", "sprop-sps", "sprop-pps" })
        {
            var match = Regex.Match(section, Regex.Escape(attribute) + @"=([^;\s]+)", RegexOptions.IgnoreCase);
            if (match.Success)
                foreach (var value in match.Groups[1].Value.Split(',')) parameters.Add(Convert.FromBase64String(value));
        }
        return new(header, track, int.Parse(map.Groups[1].Value), map.Groups[2].Value.ToUpperInvariant(), parameters);
    }

    internal static void EnsureSameServer(Uri original, Uri target)
    {
        if (target.Scheme != "rtsp" || target.Host != original.Host || target.Port != original.Port || target.UserInfo.Length > 0)
            throw new IOException("RTSP: cross-server control URLs are unsupported.");
    }
}
