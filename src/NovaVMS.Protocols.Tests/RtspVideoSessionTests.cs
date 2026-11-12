using System.Net;
using System.Net.Sockets;
using System.Text;
using NovaVMS.Protocols.Rtsp;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class RtspVideoSessionTests
{
    private static readonly byte[] Sps = [0x67, 0x42, 0x00, 0x1f];
    private static readonly byte[] Pps = [0x68, 0xce, 0x3c, 0x80];

    [Fact]
    public async Task AuthenticatesSetsUpInterleavedTrackAndDeliversPictures()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new List<string>();
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);

            while (true)
            {
                var request = await ReadRequestAsync(reader);

                if (request is null)
                {
                    return;
                }

                requests.Add(request);

                var cseq = request.Split("\r\n").First(line => line.StartsWith("CSeq:"))[5..].Trim();
                var method = request.Split(' ')[0];
                var authorized = request.Contains("Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret")));

                string response;

                if (!authorized)
                {
                    response = $"RTSP/1.0 401 Unauthorized\r\nCSeq: {cseq}\r\nWWW-Authenticate: Basic realm=\"cam\"\r\n\r\n";
                }
                else if (method == "DESCRIBE")
                {
                    var sdp = $"v=0\r\nm=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\na=fmtp:96 sprop-parameter-sets={Convert.ToBase64String(Sps)},{Convert.ToBase64String(Pps)}\r\na=control:trackID=1\r\n";

                    response = $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nContent-Base: rtsp://127.0.0.1:{port}/live/\r\nContent-Length: {sdp.Length}\r\n\r\n{sdp}";
                }
                else if (method == "SETUP")
                {
                    response = $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nSession: 1234;timeout=60\r\nTransport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n\r\n";
                }
                else
                {
                    response = $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nSession: 1234\r\n\r\n";
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes(response));

                if (method == "PLAY")
                {
                    await stream.WriteAsync(Interleaved(1, RtpVideoAssemblerTests.Rtp(1, 0, true, [0x65, 0x88])));
                    await stream.WriteAsync(Interleaved(0, RtpVideoAssemblerTests.Rtp(2, 3000, true, [0x41, 0x9a])));
                    await stream.WriteAsync(Interleaved(0, RtpVideoAssemblerTests.Rtp(3, 6000, true, [0x65, 0x88])));
                    await stream.WriteAsync(Interleaved(0, RtpVideoAssemblerTests.Rtp(4, 9000, true, [0x41, 0x9b])));
                }

                if (method == "TEARDOWN")
                {
                    return;
                }
            }
        });

        var pictures = new List<VideoAccessUnit>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = new RtspVideoSession(new Uri($"rtsp://127.0.0.1:{port}/live"), "admin", "secret", picture =>
        {
            lock (pictures)
            {
                pictures.Add(picture);

                if (pictures.Count == 2)
                {
                    stop.Cancel();
                }
            }
        });

        await session.RunAsync(stop.Token);
        await server.WaitAsync(TimeSpan.FromSeconds(5));

        // Packets of channel 1 (RTCP) are ignored; the stream starts at the first key frame.
        Assert.Equal(2, pictures.Count);
        Assert.True(pictures[0].IsKeyFrame);
        Assert.Equal([Sps, Pps], pictures[0].ParameterSets);
        Assert.Equal(["DESCRIBE", "DESCRIBE", "SETUP", "PLAY", "TEARDOWN"], requests.Select(request => request.Split(' ')[0]));
        Assert.StartsWith($"SETUP rtsp://127.0.0.1:{port}/live/trackID=1 ", requests[2]);
        Assert.Contains("Session: 1234\r\n", requests[3]);
    }

    [Fact]
    public async Task WrongPasswordFails()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);

            while (await ReadRequestAsync(reader) is { } request)
            {
                var cseq = request.Split("\r\n").First(line => line.StartsWith("CSeq:"))[5..].Trim();

                await stream.WriteAsync(Encoding.ASCII.GetBytes($"RTSP/1.0 401 Unauthorized\r\nCSeq: {cseq}\r\nWWW-Authenticate: Basic realm=\"cam\"\r\n\r\n"));
            }
        });

        var session = new RtspVideoSession(new Uri($"rtsp://127.0.0.1:{port}/live"), "admin", "wrong", _ => { });

        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(CancellationToken.None));
    }

    private static async Task<string?> ReadRequestAsync(StreamReader reader)
    {
        var request = new StringBuilder();

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0)
            {
                return request.ToString();
            }

            request.Append(line).Append("\r\n");
        }

        return null;
    }

    private static byte[] Interleaved(byte channel, byte[] packet)
    {
        return [(byte)'$', channel, (byte)(packet.Length >> 8), (byte)packet.Length, .. packet];
    }
}
