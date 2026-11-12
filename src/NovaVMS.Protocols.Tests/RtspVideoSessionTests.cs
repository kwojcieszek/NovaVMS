using System.Net;
using System.Net.Sockets;
using System.Text;
using NovaVMS.Protocols.Rtsp;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class RtpVideoAssemblerTests
{
    private static readonly byte[] Sps = [0x67, 0x42, 0x00, 0x1f];
    private static readonly byte[] Pps = [0x68, 0xce, 0x3c, 0x80];
    private static readonly byte[] Idr = [0x65, 0x88, 0x84, 0x00, 0x10];
    private static readonly byte[] Slice = [0x41, 0x9a, 0x02, 0x03];

    [Fact]
    public void WaitsForKeyFrameWithParameterSetsAndKeepsThemOutOfPictures()
    {
        var pictures = new List<VideoAccessUnit>();
        var assembler = new RtpVideoAssembler(VideoCodec.H264, 96, [Sps, Pps], pictures.Add);

        assembler.Accept(Rtp(1, 3000, true, Slice));
        assembler.Accept(Rtp(2, 6000, true, Idr));
        assembler.Accept(Rtp(3, 9000, true, Slice));

        Assert.Equal(2, pictures.Count);
        Assert.True(pictures[0].IsKeyFrame);
        Assert.Equal([Sps, Pps], pictures[0].ParameterSets);
        Assert.Equal(Idr, Assert.Single(pictures[0].Nals));
        Assert.False(pictures[1].IsKeyFrame);
        Assert.Equal(3000, pictures[1].Timestamp - pictures[0].Timestamp);
        Assert.Same(pictures[0].ParameterSets, pictures[1].ParameterSets);
    }

    [Fact]
    public void ReadsInBandParameterSetsFromAggregationAndJoinsFragments()
    {
        var pictures = new List<VideoAccessUnit>();
        var assembler = new RtpVideoAssembler(VideoCodec.H264, 96, [], pictures.Add);
        var stap = new byte[] { 24 }.Concat(Sized(Sps)).Concat(Sized(Pps)).ToArray();

        assembler.Accept(Rtp(10, 0, false, stap));
        // FU-A: indicator keeps NRI, header carries start/end and the type of the IDR slice.
        assembler.Accept(Rtp(11, 0, false, [0x7c, 0x85, .. Idr[1..3]]));
        assembler.Accept(Rtp(12, 0, true, [0x7c, 0x45, .. Idr[3..]]));

        var picture = Assert.Single(pictures);

        Assert.True(picture.IsKeyFrame);
        Assert.Equal([Sps, Pps], picture.ParameterSets);
        Assert.Equal((byte[])[0x65, .. Idr[1..]], Assert.Single(picture.Nals));
        Assert.Equal(Idr.Length + 4, picture.ToLengthPrefixed().Length);
    }

    [Fact]
    public void LostPacketDropsPicturesUntilNextKeyFrame()
    {
        var pictures = new List<VideoAccessUnit>();
        var assembler = new RtpVideoAssembler(VideoCodec.H264, 96, [Sps, Pps], pictures.Add);

        assembler.Accept(Rtp(1, 0, true, Idr));
        assembler.Accept(Rtp(3, 3000, true, Slice));
        assembler.Accept(Rtp(4, 6000, true, Slice));
        assembler.Accept(Rtp(5, 9000, true, Idr));

        Assert.Equal(2, pictures.Count);
        Assert.All(pictures, picture => Assert.True(picture.IsKeyFrame));
        Assert.True(assembler.DroppedPictures >= 1);
    }

    [Fact]
    public void JoinsHevcFragmentsAndNeedsVideoParameterSet()
    {
        byte[] vps = [0x40, 0x01, 0x0c], sps = [0x42, 0x01, 0x01], pps = [0x44, 0x01, 0xc0];
        var pictures = new List<VideoAccessUnit>();
        var assembler = new RtpVideoAssembler(VideoCodec.H265, 96, [sps, pps], pictures.Add);
        // IDR_W_RADL (type 19) split into an FU (type 49) with start and end flags.
        byte[] first = [49 << 1, 0x01, 0x80 | 19, 0xaa, 0xbb];
        byte[] last = [49 << 1, 0x01, 0x40 | 19, 0xcc];

        assembler.Accept(Rtp(1, 0, false, first));
        assembler.Accept(Rtp(2, 0, true, last));
        Assert.Empty(pictures);

        assembler.Accept(Rtp(3, 3000, false, vps));
        assembler.Accept(Rtp(4, 3000, false, first));
        assembler.Accept(Rtp(5, 3000, true, last));

        var picture = Assert.Single(pictures);

        Assert.Equal([vps, sps, pps], picture.ParameterSets);
        Assert.Equal(new byte[] { 19 << 1, 0x01, 0xaa, 0xbb, 0xcc }, Assert.Single(picture.Nals));
    }

    [Fact]
    public void DescriptionReadsCodecTrackAndParameterSets()
    {
        var sdp = "v=0\r\nm=audio 0 RTP/AVP 0\r\na=control:trackID=2\r\nm=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\n"
            + $"a=fmtp:96 packetization-mode=1;sprop-parameter-sets={Convert.ToBase64String(Sps)},{Convert.ToBase64String(Pps)}\r\na=control:trackID=1\r\n";

        var description = RtspVideoDescription.Parse(sdp, new Uri("rtsp://camera:554/Streaming/Channels/101"));

        Assert.Equal(VideoCodec.H264, description.Codec);
        Assert.Equal(96, description.PayloadType);
        Assert.Equal("rtsp://camera:554/Streaming/Channels/101/trackID=1", description.Track.AbsoluteUri);
        Assert.Equal([Sps, Pps], description.ParameterSets);
    }

    [Fact]
    public void DescriptionRejectsCodecsThePhoneCannotDecode()
    {
        const string sdp = "v=0\r\nm=video 0 RTP/AVP 26\r\na=rtpmap:26 JPEG/90000\r\na=control:trackID=1\r\n";

        Assert.Throws<NotSupportedException>(() => RtspVideoDescription.Parse(sdp, new Uri("rtsp://camera/")));
    }

    [Fact]
    public void ForeignPayloadSharingSequenceNumbersIsNotLoss()
    {
        var pictures = new List<VideoAccessUnit>();
        var assembler = new RtpVideoAssembler(VideoCodec.H264, 96, [Sps, Pps], pictures.Add);

        assembler.Accept(Rtp(1, 3000, true, Idr));
        assembler.Accept(Rtp(2, 3000, true, [0x01, 0x02], payloadType: 112));
        assembler.Accept(Rtp(3, 6000, true, Slice));
        assembler.Accept(Rtp(4, 9000, true, Slice));

        Assert.Equal(3, pictures.Count);
        Assert.Equal(0, assembler.DroppedPictures);
    }

    internal static byte[] Rtp(ushort sequence, uint timestamp, bool marker, byte[] payload, int payloadType = 96)
    {
        var packet = new byte[12 + payload.Length];

        packet[0] = 0x80;
        packet[1] = (byte)((marker ? 0x80 : 0) | payloadType);
        packet[2] = (byte)(sequence >> 8);
        packet[3] = (byte)sequence;
        packet[4] = (byte)(timestamp >> 24);
        packet[5] = (byte)(timestamp >> 16);
        packet[6] = (byte)(timestamp >> 8);
        packet[7] = (byte)timestamp;
        payload.CopyTo(packet, 12);

        return packet;
    }

    private static byte[] Sized(byte[] nal) => [(byte)(nal.Length >> 8), (byte)nal.Length, .. nal];
}

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
