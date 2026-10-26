using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Direct RTSP-over-TCP transport for encrypted Hikvision video. Credentials remain in
/// authentication headers; only the stream decryption key is passed to PlayCtrl.
/// Each playback attempt owns its socket, RTSP session, RTP assembler and decoder port.
/// </summary>
internal sealed class HikvisionRtspVideoStream : BlockingPipeStream, IVideoStreamStatus
{
    private static readonly ConcurrentDictionary<HikvisionRtspVideoStream, byte> Active = new();
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);
    // Interleaved frames carry a 16-bit length, so one buffer holds any packet.
    private readonly byte[] _packet = new byte[ushort.MaxValue];
    private readonly byte[] _oneByte = new byte[1];
    private readonly byte[] _framePrefix = new byte[3];
    private CancellationTokenSource? _idle;
    private readonly Uri _uri;
    private readonly string _username, _password, _key;
    private readonly bool _playback;
    private readonly IPlayCtrlApi _api;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TcpClient _socket = new() { NoDelay = true };
    private readonly Task _worker;
    internal Task Completion => _worker;
    private Stream? _reader;
    private NetworkStream? _network;
    private HikvisionStreamDecoder? _decoder;
    private HikvisionRtpVideo? _video;
    private RtspAuthentication? _auth;
    private string? _session, _failure;
    private int _cseq, _disposed, _videoChannel;
    private int _keepAliveSeconds = 20;
    public string? ErrorMessage => _failure ?? (_decoder is { HasFrames: false } ? ProtocolText.Get("StreamDecryptionNoFrames") : null);

    public static VideoSource CreateSource(Uri uri, Device device, bool playback)
    {
        var clean = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        return VideoSource.FromStream(() => Open(clean, device.Username, device.Password, device.StreamEncryptionKey, playback));
    }

    internal static HikvisionRtspVideoStream Open(Uri uri, string username, string password, string key, bool playback, IPlayCtrlApi? api = null)
    {
        if (uri.Scheme != "rtsp" || uri.UserInfo.Length > 0) throw new NotSupportedException(ProtocolText.Get("StreamRtspUnsupported"));
        var validation = HikvisionStreamDecoder.EncodeKey(key);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(validation);
        try { api ??= PlayCtrlApi.Get(); }
        catch (DeviceCommunicationException exception) { throw new NotSupportedException(exception.Message, exception); }
        return new(uri, username, password, key, playback, api);
    }

    private HikvisionRtspVideoStream(Uri uri, string username, string password, string key, bool playback, IPlayCtrlApi api)
        : base(64 * 1024 * 1024, false, TimeSpan.FromSeconds(15))
    {
        _uri = uri;
        _username = username;
        _password = password;
        _key = key;
        _playback = playback;
        _api = api;
        Active[this] = 0;
        _worker = Task.Run(RunAsync);
    }

    public static void StopAll()
    {
        var streams = Active.Keys.ToArray();
        foreach (var stream in streams) stream.Dispose();
        Task.WhenAll(streams.Select(stream => stream._worker)).GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        Task? keepAlive = null;
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await _socket.ConnectAsync(_uri.Host, _uri.Port > 0 ? _uri.Port : 554, connect.Token);
            }
            _network = _socket.GetStream();
            _reader = new BufferedStream(_network, 65536);
            _idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var describe = await RequestAsync("DESCRIBE", _uri, "Accept: application/sdp\r\n");
            var baseUri = _uri;
            if (describe.Headers.TryGetValue("Content-Base", out var contentBase))
            {
                baseUri = new Uri(_uri, contentBase);
                HikvisionRtspDescription.EnsureSameServer(_uri, baseUri);
            }
            var description = HikvisionRtspDescription.Parse(Encoding.UTF8.GetString(describe.Body), baseUri);
            _decoder = new HikvisionStreamDecoder(_api, _key, _playback, Append, message =>
            {
                _failure = message;
                Complete();
                _stop.Cancel();
            });
            _decoder.Feed(HcNetSdkConstants.DataSystemHeader, description.Header);
            _video = new(description.Codec, description.PayloadType, description.Parameters,
                bytes => _decoder.Feed(HcNetSdkConstants.DataStream, bytes));
            var setup = await RequestAsync("SETUP", description.Track, "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n");
            if (!setup.Headers.TryGetValue("Session", out var session)) throw new IOException("RTSP: session identifier missing.");
            _session = session.Split(';')[0].Trim();
            if (_session.Length == 0 || _session.Any(char.IsControl)) throw new IOException("RTSP: invalid session identifier.");
            var timeout = Regex.Match(session, @"timeout=(\d+)", RegexOptions.IgnoreCase);
            if (timeout.Success) _keepAliveSeconds = Math.Clamp(int.Parse(timeout.Groups[1].Value) / 2, 1, 20);
            var transport = setup.Headers.GetValueOrDefault("Transport", "");
            var channels = Regex.Match(transport, @"interleaved=(\d+)-(\d+)", RegexOptions.IgnoreCase);
            if (!transport.Contains("RTP/AVP/TCP", StringComparison.OrdinalIgnoreCase) || !channels.Success)
                throw new IOException("RTSP: TCP interleaving was not negotiated.");
            _videoChannel = int.Parse(channels.Groups[1].Value);
            if (_videoChannel > 255 || int.Parse(channels.Groups[2].Value) > 255) throw new IOException("RTSP: invalid interleaved channels.");
            await RequestAsync("PLAY", _uri, "Range: npt=0.000-\r\n");
            keepAlive = KeepAliveAsync();
            while (true)
            {
                var response = await ReadResponseAsync();
                if (response.Status != 200) throw new IOException("RTSP: session keep-alive failed.");
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (EndOfStreamException) when (_decoder is { HasFrames: true }) { }
        catch (Exception)
        {
            // Do not surface raw RTSP messages, credentials, URLs, or packet contents.
            _failure ??= ProtocolText.Get("StreamRtspFailed");
        }
        finally
        {
            Complete();
            _stop.Cancel();
            if (keepAlive is not null)
                try { await keepAlive; } catch (Exception) { }
            if (_session is not null && _network is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await WriteRequestAsync("TEARDOWN", _uri, "", timeout.Token); } catch (Exception) { }
            }
            _socket.Dispose();
            _idle?.Dispose();
            _decoder?.Dispose();
            Active.TryRemove(this, out _);
        }
    }

    private async Task KeepAliveAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_keepAliveSeconds), _stop.Token);
                await WriteRequestAsync("OPTIONS", _uri, "", _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception)
        {
            _failure ??= ProtocolText.Get("StreamRtspFailed");
            Complete();
            _stop.Cancel();
        }
    }

    private async Task<RtspResponse> RequestAsync(string method, Uri uri, string extra)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var cseq = await WriteRequestAsync(method, uri, extra, _stop.Token);
            var response = await ReadResponseAsync();
            if (response.Headers.GetValueOrDefault("CSeq") != cseq.ToString(CultureInfo.InvariantCulture))
                throw new IOException("RTSP: response sequence mismatch.");
            if (response.Status == 401 && attempt == 0 && response.Headers.TryGetValue("WWW-Authenticate", out var challenge))
            {
                _auth = new RtspAuthentication(challenge, _username, _password);
                continue;
            }
            if (response.Status != 200) throw new IOException("RTSP: request rejected.");
            return response;
        }
        throw new IOException("RTSP: authentication failed.");
    }

    private async Task<int> WriteRequestAsync(string method, Uri uri, string extra, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var cseq = ++_cseq;
            var request = new StringBuilder($"{method} {uri.AbsoluteUri} RTSP/1.0\r\nCSeq: {cseq}\r\nUser-Agent: NovaVMS\r\n");
            if (_auth is not null) request.Append("Authorization: ").Append(_auth.Build(method, uri)).Append("\r\n");
            if (_session is not null) request.Append("Session: ").Append(_session).Append("\r\n");
            request.Append(extra).Append("\r\n");
            await _network!.WriteAsync(Encoding.UTF8.GetBytes(request.ToString()), cancellationToken);
            return cseq;
        }
        finally { _writeLock.Release(); }
    }

    private async Task<RtspResponse> ReadResponseAsync()
    {
        var one = _oneByte;
        while (true)
        {
            await ReadWireExactlyAsync(one);
            if (one[0] == '$')
            {
                var prefix = _framePrefix;
                await ReadWireExactlyAsync(prefix);
                var length = (prefix[1] << 8) | prefix[2];
                // The depacketizer copies what it keeps, so the packet buffer is reused.
                await ReadWireExactlyAsync(_packet.AsMemory(0, length));
                if (prefix[0] == _videoChannel) _video?.Accept(_packet.AsSpan(0, length));
                continue;
            }
            using var header = new MemoryStream();
            header.WriteByte(one[0]);
            var matched = 0;
            while (matched != 4)
            {
                if (header.Length > 32768) throw new IOException("RTSP headers too large.");
                await ReadWireExactlyAsync(one);
                header.WriteByte(one[0]);
                matched = one[0] == "\r\n\r\n"[matched] ? matched + 1 : (one[0] == '\r' ? 1 : 0);
            }
            var lines = Encoding.UTF8.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var status = lines[0].Split(' ');
            if (status.Length < 2 || status[0] != "RTSP/1.0" || !int.TryParse(status[1], out var code))
                throw new IOException("Invalid RTSP response.");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) throw new IOException("Invalid RTSP response header.");
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (!headers.ContainsKey(name) || (name.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase) && value.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase)))
                    headers[name] = value;
            }
            var bodyLength = 0;
            if (headers.TryGetValue("Content-Length", out var contentLength)
                && (!int.TryParse(contentLength, out bodyLength) || bodyLength is < 0 or > 65536))
                throw new IOException("Invalid RTSP response length.");
            var body = new byte[bodyLength];
            await ReadWireExactlyAsync(body);
            return new(code, headers, body);
        }
    }

    private async Task ReadWireExactlyAsync(Memory<byte> buffer)
    {
        // One timer re-armed before every read: the stream ends when the device stops sending,
        // without allocating a token source per RTP packet.
        var idle = _idle!;
        idle.CancelAfter(IdleTimeout);
        await _reader!.ReadExactlyAsync(buffer, idle.Token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Complete();
            _stop.Cancel();
            _decoder?.Stop();
        }
        base.Dispose(disposing);
    }

    private sealed record RtspResponse(int Status, Dictionary<string, string> Headers, byte[] Body);
}
