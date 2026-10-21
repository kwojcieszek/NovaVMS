using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.HcNetSdk;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.Onvif;

/// <summary>
/// Adapts the player's RTSP session to Profile G's absolute clock range. RTP/RTCP,
/// including audio and original timestamps, pass through unchanged. No vendor SDK is used.
/// </summary>
internal sealed class OnvifReplayProxy : IDisposable, IVideoStreamStatus
{
    private readonly Uri _remote;
    private readonly string _username, _password;
    private readonly DateTime _start, _end;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _remoteWrite = new(1, 1);
    private readonly ConcurrentDictionary<string, (Message Request, bool Retried)> _requests = new();
    private readonly Task _worker;
    private TcpClient? _localSocket, _remoteSocket;
    private RtspAuthentication? _authentication;
    private bool _played;
    private int _disposed;
    public Uri Uri { get; }
    public string? ErrorMessage { get; private set; }
    internal Task Completion => _worker;

    public OnvifReplayProxy(Uri remote, string username, string password, DateTime startUtc, DateTime endUtc)
    {
        if (remote.Scheme != "rtsp" || remote.UserInfo.Length != 0 || startUtc >= endUtc)
            throw new ArgumentException("Invalid ONVIF replay source.");
        _remote = remote;
        _username = username;
        _password = password;
        _start = startUtc;
        _end = endUtc;
        _listener.Start(1);
        Uri = new UriBuilder(remote) { Host = "127.0.0.1", Port = ((IPEndPoint)_listener.LocalEndpoint).Port }.Uri;
        _worker = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            _localSocket = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            _listener.Stop(); // A playback attempt owns exactly one client connection.
            _localSocket.NoDelay = true;
            _remoteSocket = new TcpClient { NoDelay = true };
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await _remoteSocket.ConnectAsync(_remote.Host, _remote.Port > 0 ? _remote.Port : 554, connect.Token).ConfigureAwait(false);
            }
            var local = _localSocket.GetStream();
            var remote = _remoteSocket.GetStream();
            var outbound = ForwardRequestsAsync(local, remote);
            var inbound = ForwardResponsesAsync(remote, local);
            await Task.WhenAny(outbound, inbound).ConfigureAwait(false);
            _stop.Cancel();
            await Task.WhenAll(outbound, inbound).ConfigureAwait(false);
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
        catch (EndOfStreamException) { }
        catch (Exception)
        {
            // Raw replies and URI query strings may contain credentials or device secrets.
            ErrorMessage = ProtocolText.Get("StreamRtspFailed");
        }
        finally
        {
            _stop.Cancel();
            _listener.Stop();
            _localSocket?.Dispose();
            _remoteSocket?.Dispose();
        }
    }

    private async Task ForwardRequestsAsync(Stream local, Stream remote)
    {
        while (true)
        {
            var message = await ReadAsync(local, _stop.Token).ConfigureAwait(false);
            if (message.Binary is null)
            {
                var parts = message.Line.Split(' ', 3);
                if (parts.Length != 3) throw new IOException("Invalid RTSP request.");
                var target = parts[1] == "*" ? _remote : RemoteUri(new Uri(parts[1]));
                message.Line = $"{parts[0]} {(parts[1] == "*" ? "*" : target.AbsoluteUri)} RTSP/1.0";
                message.Headers.Remove("Authorization");
                if (parts[0] == "PLAY")
                {
                    message.Headers["Require"] = "onvif-replay";
                    message.Headers["Rate-Control"] = "yes";
                    message.Headers["Scale"] = "1.0";
                    message.Headers.Remove("Range");
                    if (!_played)
                    {
                        message.Headers["Range"] = $"clock={Clock(_start)}-{Clock(_end)}";
                        _played = true;
                    }
                }
                if (_authentication is not null)
                    message.Headers["Authorization"] = _authentication.Build(parts[0], target);
                _requests[message.Headers["CSeq"]] = (message, false);
            }
            await WriteRemoteAsync(remote, message).ConfigureAwait(false);
        }
    }

    private async Task ForwardResponsesAsync(Stream remote, Stream local)
    {
        while (true)
        {
            var message = await ReadAsync(remote, _stop.Token).ConfigureAwait(false);
            if (message.Binary is null)
            {
                var sequence = message.Headers.GetValueOrDefault("CSeq", "");
                if (_requests.TryGetValue(sequence, out var pending))
                {
                    if (message.Line.StartsWith("RTSP/1.0 401 ", StringComparison.Ordinal) && !pending.Retried
                        && message.Headers.TryGetValue("WWW-Authenticate", out var challenge))
                    {
                        _authentication = new RtspAuthentication(challenge, _username, _password);
                        var parts = pending.Request.Line.Split(' ', 3);
                        var target = parts[1] == "*" ? _remote : new Uri(parts[1]);
                        pending.Request.Headers["Authorization"] = _authentication.Build(parts[0], target);
                        _requests[sequence] = (pending.Request, true);
                        await WriteRemoteAsync(remote, pending.Request).ConfigureAwait(false);
                        continue;
                    }
                    _requests.TryRemove(sequence, out _);
                }
                foreach (var name in new[] { "Content-Base", "Content-Location", "Location" })
                    if (message.Headers.TryGetValue(name, out var value)) message.Headers[name] = LocalAddresses(value);
                // LibVLC tracks elapsed playback time as NPT. The remote session uses UTC.
                if (message.Headers.GetValueOrDefault("Range", "").StartsWith("clock=", StringComparison.OrdinalIgnoreCase))
                    message.Headers["Range"] = "npt=0-";
                if (message.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/sdp", StringComparison.OrdinalIgnoreCase))
                {
                    var sdp = LocalAddresses(Encoding.UTF8.GetString(message.Body));
                    sdp = Regex.Replace(sdp, @"a=range:clock=[^\r\n]*", "a=range:npt=0-", RegexOptions.IgnoreCase);
                    message.Body = Encoding.UTF8.GetBytes(sdp);
                }
            }
            await WriteAsync(local, message, _stop.Token).ConfigureAwait(false);
        }
    }

    private Uri RemoteUri(Uri local)
    {
        if (local.Scheme != "rtsp" || local.Host != Uri.Host || local.Port != Uri.Port)
            throw new IOException("Unexpected RTSP control address.");
        return new UriBuilder(local) { Host = _remote.Host, Port = _remote.Port }.Uri;
    }

    private string LocalAddresses(string text) => Regex.Replace(text, @"rtsp://[^\s<>""']+", match =>
    {
        if (!System.Uri.TryCreate(match.Value, UriKind.Absolute, out var address)) return match.Value;
        if (address.UserInfo.Length > 0)
            throw new IOException("Unexpected RTSP media address.");
        // NAT devices can report internal hosts/ports in SDP. All outgoing requests
        // still use the originally configured recorder connection in RemoteUri.
        return new UriBuilder(address) { Host = Uri.Host, Port = Uri.Port }.Uri.AbsoluteUri;
    }, RegexOptions.IgnoreCase);

    private async Task WriteRemoteAsync(Stream stream, Message message)
    {
        await _remoteWrite.WaitAsync(_stop.Token).ConfigureAwait(false);
        try { await WriteAsync(stream, message, _stop.Token).ConfigureAwait(false); }
        finally { _remoteWrite.Release(); }
    }

    private sealed class Message
    {
        public string Line = "";
        public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = [];
        public byte[]? Binary;
    }

    private static async Task<Message> ReadAsync(Stream stream, CancellationToken token)
    {
        var one = new byte[1];
        await stream.ReadExactlyAsync(one, token).ConfigureAwait(false);
        if (one[0] == '$')
        {
            var prefix = new byte[3];
            await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
            var frame = new byte[4 + (prefix[1] << 8) + prefix[2]];
            frame[0] = (byte)'$';
            prefix.CopyTo(frame, 1);
            await stream.ReadExactlyAsync(frame.AsMemory(4), token).ConfigureAwait(false);
            return new Message { Binary = frame };
        }
        using var header = new MemoryStream();
        header.WriteByte(one[0]);
        var matched = 0;
        while (matched < 4)
        {
            await stream.ReadExactlyAsync(one, token).ConfigureAwait(false);
            header.WriteByte(one[0]);
            matched = one[0] == "\r\n\r\n"[matched] ? matched + 1 : one[0] == '\r' ? 1 : 0;
            if (header.Length > 65536) throw new IOException("RTSP header too large.");
        }
        var lines = Encoding.UTF8.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var message = new Message { Line = lines[0] };
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) throw new IOException("Invalid RTSP header.");
            message.Headers[line[..separator]] = line[(separator + 1)..].Trim();
        }
        var length = int.Parse(message.Headers.GetValueOrDefault("Content-Length", "0"), CultureInfo.InvariantCulture);
        if (length is < 0 or > 1024 * 1024) throw new IOException("RTSP body too large.");
        message.Body = new byte[length];
        await stream.ReadExactlyAsync(message.Body, token).ConfigureAwait(false);
        return message;
    }

    private static async Task WriteAsync(Stream stream, Message message, CancellationToken token)
    {
        if (message.Binary is not null) { await stream.WriteAsync(message.Binary, token).ConfigureAwait(false); return; }
        if (message.Body.Length > 0 || message.Headers.ContainsKey("Content-Length"))
            message.Headers["Content-Length"] = message.Body.Length.ToString(CultureInfo.InvariantCulture);
        var header = message.Line + "\r\n" + string.Concat(message.Headers.Select(h => $"{h.Key}: {h.Value}\r\n")) + "\r\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(header), token).ConfigureAwait(false);
        await stream.WriteAsync(message.Body, token).ConfigureAwait(false);
    }

    private static string Clock(DateTime value) => value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _listener.Stop();
        _localSocket?.Dispose();
        _remoteSocket?.Dispose();
    }
}
