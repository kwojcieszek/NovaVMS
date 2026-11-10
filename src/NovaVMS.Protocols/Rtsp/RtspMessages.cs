using System.Text;
using System.Text.RegularExpressions;
using NovaVMS.Protocols.HcNetSdk;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>
/// RTSP requests and responses on a TCP connection that also carries the interleaved RTP packets,
/// shared by the RTSP clients of the solution.
/// </summary>
internal static class RtspMessages
{
    /// <summary>Largest RTP packet of an interleaved frame (the length field has 16 bits).</summary>
    public const int MaxPacketLength = ushort.MaxValue;

    private const int MaxHeaderLength = 32768;
    private const int MaxBodyLength = 65536;

    /// <summary>Builds a request; <paramref name="extra"/> holds further header lines, each ending with CRLF.</summary>
    public static byte[] Request(string method, Uri uri, int cseq, RtspAuthentication? authentication, string? session, string extra)
    {
        var request = new StringBuilder($"{method} {uri.AbsoluteUri} RTSP/1.0\r\nCSeq: {cseq}\r\nUser-Agent: NovaVMS\r\n");

        if (authentication is not null)
        {
            request.Append("Authorization: ").Append(authentication.Build(method, uri)).Append("\r\n");
        }

        if (session is not null)
        {
            request.Append("Session: ").Append(session).Append("\r\n");
        }

        request.Append(extra).Append("\r\n");

        return Encoding.UTF8.GetBytes(request.ToString());
    }

    /// <summary>
    /// Reads the next response. Interleaved frames read before it go to <paramref name="onPacket"/> with their channel;
    /// the packet buffer is reused, so the handler copies what it keeps.
    /// </summary>
    /// <param name="readExactly">Fills the whole buffer from the connection.</param>
    /// <param name="oneByte">Buffer of one byte.</param>
    /// <param name="framePrefix">Buffer of three bytes for the channel and length of a frame.</param>
    /// <param name="packet">Buffer of <see cref="MaxPacketLength"/> bytes for the RTP packets.</param>
    /// <param name="onPacket">Receives the channel and the packet of every interleaved frame.</param>
    public static async Task<RtspResponse> ReadResponseAsync(
        Func<Memory<byte>, Task> readExactly,
        byte[] oneByte,
        byte[] framePrefix,
        byte[] packet,
        Action<byte, ArraySegment<byte>> onPacket)
    {
        while (true)
        {
            await readExactly(oneByte).ConfigureAwait(false);

            if (oneByte[0] == '$')
            {
                await readExactly(framePrefix).ConfigureAwait(false);

                var length = (framePrefix[1] << 8) | framePrefix[2];

                await readExactly(packet.AsMemory(0, length)).ConfigureAwait(false);

                onPacket(framePrefix[0], new ArraySegment<byte>(packet, 0, length));

                continue;
            }

            var header = await ReadHeaderAsync(readExactly, oneByte).ConfigureAwait(false);
            var (status, headers) = ParseHeader(header);
            var bodyLength = 0;

            if (headers.TryGetValue("Content-Length", out var contentLength)
                && (!int.TryParse(contentLength, out bodyLength) || bodyLength is < 0 or > MaxBodyLength))
            {
                throw new IOException("Invalid RTSP response length.");
            }

            var body = new byte[bodyLength];

            await readExactly(body).ConfigureAwait(false);

            return new RtspResponse(status, headers, body);
        }
    }

    /// <summary>
    /// The session identifier of a SETUP response and, when the server gives a timeout, the keep-alive interval:
    /// half of the timeout, at most <paramref name="maxKeepAliveSeconds"/>.
    /// </summary>
    public static (string Id, int? KeepAliveSeconds) ReadSession(RtspResponse setup, int maxKeepAliveSeconds)
    {
        if (!setup.Headers.TryGetValue("Session", out var session))
        {
            throw new IOException("RTSP: session identifier missing.");
        }

        var id = session.Split(';')[0].Trim();

        if (id.Length == 0 || id.Any(char.IsControl))
        {
            throw new IOException("RTSP: invalid session identifier.");
        }

        var timeout = Regex.Match(session, @"timeout=(\d+)", RegexOptions.IgnoreCase);
        int? keepAlive = timeout.Success && int.TryParse(timeout.Groups[1].Value, out var seconds)
            ? Math.Clamp(seconds / 2, 1, maxKeepAliveSeconds)
            : null;

        return (id, keepAlive);
    }

    // The status line and headers end with an empty line; the first byte was already read.
    private static async Task<byte[]> ReadHeaderAsync(Func<Memory<byte>, Task> readExactly, byte[] oneByte)
    {
        using var header = new MemoryStream();

        header.WriteByte(oneByte[0]);

        var matched = 0;

        while (matched != 4)
        {
            if (header.Length > MaxHeaderLength)
            {
                throw new IOException("RTSP headers too large.");
            }

            await readExactly(oneByte).ConfigureAwait(false);

            header.WriteByte(oneByte[0]);

            matched = oneByte[0] == "\r\n\r\n"[matched] ? matched + 1 : (oneByte[0] == '\r' ? 1 : 0);
        }

        return header.ToArray();
    }

    private static (int Status, Dictionary<string, string> Headers) ParseHeader(byte[] header)
    {
        var lines = Encoding.UTF8.GetString(header).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var status = lines[0].Split(' ');

        if (status.Length < 2 || !status[0].StartsWith("RTSP/", StringComparison.Ordinal) || !int.TryParse(status[1], out var code))
        {
            throw new IOException("Invalid RTSP response.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');

            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            // Digest is preferred over Basic when a camera offers both.
            if (!headers.ContainsKey(name) || (name.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase) && value.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase)))
            {
                headers[name] = value;
            }
        }

        return (code, headers);
    }
}
