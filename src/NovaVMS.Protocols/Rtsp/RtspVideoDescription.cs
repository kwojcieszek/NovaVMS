using System.Text.RegularExpressions;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>The video track of an RTSP session description (SDP): control address, payload type, codec and parameter sets.</summary>
public sealed record RtspVideoDescription
{
    public Uri Track { get; init; }

    public int PayloadType { get; init; }

    public VideoCodec Codec { get; init; }

    public IReadOnlyList<byte[]> ParameterSets { get; init; }

    public RtspVideoDescription(Uri track, int payloadType, VideoCodec codec, IReadOnlyList<byte[]> parameterSets)
    {
        Track = track;
        PayloadType = payloadType;
        Codec = codec;
        ParameterSets = parameterSets;
    }

    public static RtspVideoDescription Parse(string sdp, Uri baseUri)
    {
        var lines = sdp.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var section = new List<string>();
        var video = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("m=", StringComparison.Ordinal))
            {
                if (video)
                {
                    break;
                }

                video = line.StartsWith("m=video ", StringComparison.OrdinalIgnoreCase);
            }

            if (video)
            {
                section.Add(line);
            }
        }

        if (section.Count == 0)
        {
            throw new IOException("RTSP: the stream has no video.");
        }

        var text = string.Join("\n", section);
        var map = Regex.Match(text, @"a=rtpmap:(\d+) (H264|H265|HEVC)/90000", RegexOptions.IgnoreCase);

        if (!map.Success || int.Parse(map.Groups[1].Value) > 127)
        {
            throw new NotSupportedException("RTSP: only H.264 and H.265 video can be decoded on this device.");
        }

        if (Regex.IsMatch(text, @"(?:packetization-mode=2|sprop-max-don-diff=[1-9])", RegexOptions.IgnoreCase))
        {
            throw new NotSupportedException("RTSP: interleaved packetization is not supported.");
        }

        var codec = map.Groups[2].Value.Equals("H264", StringComparison.OrdinalIgnoreCase) ? VideoCodec.H264 : VideoCodec.H265;
        var control = section.FirstOrDefault(line => line.StartsWith("a=control:", StringComparison.OrdinalIgnoreCase))?[10..].Trim();
        var track = baseUri;

        if (!string.IsNullOrEmpty(control) && control != "*")
        {
            if (Uri.TryCreate(control, UriKind.Absolute, out var absolute))
            {
                track = absolute;
            }
            else
            {
                var trackBase = new UriBuilder(baseUri) { Query = "", Fragment = "", Path = baseUri.AbsolutePath.TrimEnd('/') + "/" }.Uri;

                track = new Uri(trackBase, control);
            }
        }

        EnsureSameServer(baseUri, track);

        var parameters = new List<byte[]>();

        foreach (var attribute in new[] { "sprop-vps", "sprop-sps", "sprop-pps", "sprop-parameter-sets" })
        {
            var match = Regex.Match(text, Regex.Escape(attribute) + @"=([^;\s]+)", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                continue;
            }

            foreach (var value in match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    parameters.Add(Convert.FromBase64String(value));
                }
                catch (FormatException)
                {
                    // The parameter sets also arrive in the stream.
                }
            }
        }

        return new(track, int.Parse(map.Groups[1].Value), codec, parameters);
    }

    /// <summary>Control addresses must stay on the camera that was asked; credentials are never sent elsewhere.</summary>
    public static void EnsureSameServer(Uri original, Uri target)
    {
        if (target.Scheme != "rtsp" || !string.Equals(target.Host, original.Host, StringComparison.OrdinalIgnoreCase) || target.Port != original.Port)
        {
            throw new IOException("RTSP: cross-server control addresses are not supported.");
        }
    }
}
