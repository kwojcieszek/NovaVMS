using System.Net;
using System.Net.Sockets;
using System.Text;
using NovaVMS.Protocols.Onvif;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class OnvifReplayProxyTests
{
    [Fact]
    public async Task AuthenticatesWithRemoteUriRequestsClockRangeAndPreservesRtpAndResume()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var remoteUri = new Uri($"rtsp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/archive/camera2");
            var start = new DateTime(2026, 10, 20, 8, 30, 0, DateTimeKind.Utc);
            using var proxy = new OnvifReplayProxy(remoteUri, "operator", "secret", start, start.AddHours(1));
            var packet = new byte[] { (byte)'$', 0, 0, 8, 0x80, 0x60, 0, 4, 1, 2, 3, 4 };
            var server = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(token);
                var stream = socket.GetStream();
                var first = await ReadAsync(stream, token);
                Assert.StartsWith($"DESCRIBE {remoteUri.AbsoluteUri} RTSP/1.0", first.Header);
                await SendAsync(stream, "RTSP/1.0 401 Unauthorized\r\nCSeq: 1\r\nWWW-Authenticate: Digest realm=\"archive\", nonce=\"123\", qop=\"auth\"\r\n\r\n", token);
                var retry = await ReadAsync(stream, token);
                Assert.Contains("Authorization: Digest", retry.Header);
                Assert.Contains($"uri=\"{remoteUri.AbsoluteUri}\"", retry.Header);
                Assert.DoesNotContain("secret", retry.Header);
                var sdp = $"v=0\r\na=control:{remoteUri.AbsoluteUri}/track1\r\na=range:clock=20261020T083000Z-20261020T093000Z\r\n";
                await SendAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: 1\r\nContent-Base: {remoteUri.AbsoluteUri}/\r\nContent-Type: application/sdp\r\nContent-Length: {Encoding.UTF8.GetByteCount(sdp)}\r\n\r\n{sdp}", token);
                var play = await ReadAsync(stream, token);
                Assert.Contains("Require: onvif-replay", play.Header);
                Assert.Contains("Range: clock=20261020T083000Z-20261020T093000Z", play.Header);
                Assert.Contains("Rate-Control: yes", play.Header);
                Assert.Contains("Scale: 1.0", play.Header);
                await SendAsync(stream, "RTSP/1.0 200 OK\r\nCSeq: 2\r\nRange: clock=20261020T083000Z-20261020T093000Z\r\n\r\n", token);
                await stream.WriteAsync(packet, token);
                var pause = await ReadAsync(stream, token);
                Assert.StartsWith("PAUSE ", pause.Header);
                await SendAsync(stream, "RTSP/1.0 200 OK\r\nCSeq: 3\r\n\r\n", token);
                var resume = await ReadAsync(stream, token);
                Assert.StartsWith("PLAY ", resume.Header);
                Assert.DoesNotContain("Range:", resume.Header); // Resume must not restart the archive.
                await SendAsync(stream, "RTSP/1.0 200 OK\r\nCSeq: 4\r\n\r\n", token);
            }, token);
            using var client = new TcpClient();
            await client.ConnectAsync(proxy.Uri.Host, proxy.Uri.Port, token);
            var local = client.GetStream();
            await SendAsync(local, $"DESCRIBE {proxy.Uri.AbsoluteUri} RTSP/1.0\r\nCSeq: 1\r\n\r\n", token);
            var description = await ReadAsync(local, token);
            Assert.Contains($"Content-Base: {proxy.Uri.AbsoluteUri}/", description.Header);
            Assert.Contains($"a=control:{proxy.Uri.AbsoluteUri}/track1", description.Body);
            Assert.Contains("a=range:npt=0-", description.Body);
            await SendAsync(local, $"PLAY {proxy.Uri.AbsoluteUri} RTSP/1.0\r\nCSeq: 2\r\nRange: npt=0-\r\n\r\n", token);
            Assert.Contains("Range: npt=0-", (await ReadAsync(local, token)).Header);
            var forwarded = new byte[packet.Length];
            await local.ReadExactlyAsync(forwarded, token);
            Assert.Equal(packet, forwarded);
            await SendAsync(local, $"PAUSE {proxy.Uri.AbsoluteUri} RTSP/1.0\r\nCSeq: 3\r\n\r\n", token);
            await ReadAsync(local, token);
            await SendAsync(local, $"PLAY {proxy.Uri.AbsoluteUri} RTSP/1.0\r\nCSeq: 4\r\nRange: npt=0-\r\n\r\n", token);
            await ReadAsync(local, token);
            await server;
            proxy.Dispose();
            await proxy.Completion.WaitAsync(token);
            Assert.Null(proxy.ErrorMessage);
        }
        finally { listener.Stop(); }
    }

    private static async Task SendAsync(Stream stream, string message, CancellationToken token)
        => await stream.WriteAsync(Encoding.UTF8.GetBytes(message), token);

    private static async Task<(string Header, string Body)> ReadAsync(Stream stream, CancellationToken token)
    {
        var text = new StringBuilder();
        var one = new byte[1];
        while (!text.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            await stream.ReadExactlyAsync(one, token);
            text.Append((char)one[0]);
        }
        var header = text.ToString();
        var lengthLine = header.Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var body = new byte[lengthLine is null ? 0 : int.Parse(lengthLine.Split(':')[1])];
        await stream.ReadExactlyAsync(body, token);
        return (header, Encoding.UTF8.GetString(body));
    }
}
