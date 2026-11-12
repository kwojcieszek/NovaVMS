using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using NovaVMS.Protocols.HcNetSdk;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>
/// RTSP client receiving one video track over TCP (RTP interleaved in the RTSP connection) and handing whole
/// pictures to a platform decoder. Everything runs on the thread pool. Used on phones, where the RTSP library of
/// LibVLC (live555) waits seconds for a multicast test that iOS does not allow before every connection.
/// </summary>
public sealed class RtspVideoSession
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);

    // Interleaved frames carry a 16-bit length, so one buffer holds any packet.
    private readonly byte[] _packet = new byte[RtspMessages.MaxPacketLength];
    private readonly byte[] _oneByte = new byte[1];
    private readonly byte[] _framePrefix = new byte[3];
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Uri _uri;
    private readonly string _username;
    private readonly string _password;
    private readonly string? _streamKey;
    private readonly Action<VideoAccessUnit> _output;

    private Stream? _reader;
    private NetworkStream? _network;
    private CancellationTokenSource? _idle;
    private RtpVideoAssembler? _assembler;
    private RtspAuthentication? _auth;
    private string? _session;
    private int _cseq;
    private int _videoChannel = -1;
    private int _keepAliveSeconds = 20;

    /// <summary>Pictures dropped because data was lost; for diagnostics.</summary>
    public int DroppedPictures => _assembler?.DroppedPictures ?? 0;

    /// <summary>Encrypted video did not decrypt with the stream key.</summary>
    public bool StreamKeyRejected => _assembler?.StreamKeyRejected ?? false;

    /// <param name="uri">RTSP address; credentials in it are used when <paramref name="username"/> is empty and are never sent in the address.</param>
    /// <param name="output">Receives every complete picture on the receiving thread; it must not block for long.</param>
    /// <param name="streamKey">Hikvision stream encryption key from the device settings, or null or empty for clear video.</param>
    public RtspVideoSession(Uri uri, string? username, string? password, Action<VideoAccessUnit> output, string? streamKey = null)
    {
        if (uri.Scheme != "rtsp")
        {
            throw new NotSupportedException("Only RTSP addresses are supported.");
        }

        var info = uri.UserInfo.Split(':', 2);

        _username = string.IsNullOrEmpty(username) && info[0].Length > 0 ? Uri.UnescapeDataString(info[0]) : username ?? "";
        _password = string.IsNullOrEmpty(username) && info.Length > 1 ? Uri.UnescapeDataString(info[1]) : password ?? "";
        _uri = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
        _streamKey = streamKey;
        _output = output;
    }

    /// <summary>
    /// Connects, starts the stream and receives it until <paramref name="cancellationToken"/> is cancelled
    /// (returns normally) or the camera ends the stream (returns normally) or fails (throws).
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var socket = new TcpClient { NoDelay = true, ReceiveBufferSize = 1024 * 1024 };
        Task? keepAlive = null;

        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                var port = _uri.Port > 0 ? _uri.Port : 554;

                connect.CancelAfter(ConnectTimeout);

                try
                {
                    await socket.ConnectAsync(_uri.Host, port, connect.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (!stop.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"RTSP: no TCP connection to {_uri.Host}:{port} within {ConnectTimeout.TotalSeconds:0} s.", exception);
                }
                catch (SocketException exception)
                {
                    throw new IOException($"RTSP: cannot connect to {_uri.Host}:{port}: {exception.SocketErrorCode} ({exception.Message}).", exception);
                }
            }

            _network = socket.GetStream();
            _reader = new BufferedStream(_network, 256 * 1024);
            _idle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);

            var describe = await RequestAsync("DESCRIBE", _uri, "Accept: application/sdp\r\n", stop.Token).ConfigureAwait(false);
            var baseUri = _uri;

            if (describe.Headers.TryGetValue("Content-Base", out var contentBase) && Uri.TryCreate(_uri, contentBase, out var reported))
            {
                RtspVideoDescription.EnsureSameServer(_uri, reported);
                baseUri = reported;
            }

            var description = RtspVideoDescription.Parse(Encoding.UTF8.GetString(describe.Body), baseUri);

            _assembler = new RtpVideoAssembler(description.Codec, description.PayloadType, description.ParameterSets, _output, _streamKey);

            var setup = await RequestAsync("SETUP", description.Track, "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n", stop.Token).ConfigureAwait(false);

            ReadSession(setup);
            ReadTransport(setup);

            await RequestAsync("PLAY", baseUri, "Range: npt=0.000-\r\n", stop.Token).ConfigureAwait(false);

            keepAlive = KeepAliveAsync(baseUri, stop);

            while (true)
            {
                var response = await ReadResponseAsync(stop.Token).ConfigureAwait(false);

                // Some cameras answer a keep-alive OPTIONS with an error but keep sending video.
                if (response.Status is >= 500)
                {
                    throw new IOException("RTSP: the camera ended the session.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException) when (_assembler is not null)
        {
            // The camera closed the connection, e.g. at the end of a recording.
        }
        finally
        {
            stop.Cancel();

            if (keepAlive is not null)
            {
                try
                {
                    await keepAlive.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            if (_session is not null && _network is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));

                try
                {
                    await WriteRequestAsync("TEARDOWN", _uri, "", timeout.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            _idle?.Dispose();
            _assembler?.Dispose();
        }
    }

    private void ReadSession(RtspResponse setup)
    {
        var (session, keepAlive) = RtspMessages.ReadSession(setup, maxKeepAliveSeconds: 30);

        _session = session;
        _keepAliveSeconds = keepAlive ?? _keepAliveSeconds;
    }

    private void ReadTransport(RtspResponse setup)
    {
        var transport = setup.Headers.GetValueOrDefault("Transport", "");
        var channels = Regex.Match(transport, @"interleaved=(\d+)(?:-(\d+))?", RegexOptions.IgnoreCase);

        if (!channels.Success)
        {
            // The requested channels apply when the camera does not repeat them.
            _videoChannel = 0;

            return;
        }

        _videoChannel = int.Parse(channels.Groups[1].Value, CultureInfo.InvariantCulture);

        if (_videoChannel > 255)
        {
            throw new IOException("RTSP: invalid interleaved channel.");
        }
    }

    private async Task KeepAliveAsync(Uri uri, CancellationTokenSource stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_keepAliveSeconds), stop.Token).ConfigureAwait(false);
                await WriteRequestAsync("OPTIONS", uri, "", stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // A broken connection also ends the receiving loop.
            stop.Cancel();
        }
    }

    private async Task<RtspResponse> RequestAsync(string method, Uri uri, string extra, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var cseq = await WriteRequestAsync(method, uri, extra, cancellationToken).ConfigureAwait(false);
            RtspResponse response;

            // Responses to an earlier keep-alive may arrive first.
            do
            {
                response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            }
            while (response.Headers.TryGetValue("CSeq", out var answered) && answered != cseq.ToString(CultureInfo.InvariantCulture)
                && int.TryParse(answered, out var earlier) && earlier < cseq);

            if (response.Status == 401 && attempt == 0 && response.Headers.TryGetValue("WWW-Authenticate", out var challenge))
            {
                _auth = new RtspAuthentication(challenge, _username, _password);

                continue;
            }

            if (response.Status != 200)
            {
                throw new IOException(response.Status == 401 ? "RTSP: wrong user name or password." : $"RTSP: {method} rejected ({response.Status}).");
            }

            return response;
        }

        throw new IOException("RTSP: wrong user name or password.");
    }

    private async Task<int> WriteRequestAsync(string method, Uri uri, string extra, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var cseq = ++_cseq;
            var request = RtspMessages.Request(method, uri, cseq, _auth, _session, extra);

            await _network!.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            return cseq;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // The assembler copies what it keeps, so the packet buffer is reused.
    private Task<RtspResponse> ReadResponseAsync(CancellationToken cancellationToken)
    {
        return RtspMessages.ReadResponseAsync(
            buffer => ReadExactlyAsync(buffer, cancellationToken),
            _oneByte,
            _framePrefix,
            _packet,
            (channel, packet) =>
            {
                if (channel == _videoChannel)
                {
                    _assembler?.Accept(packet);
                }
            });
    }

    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        // One timer re-armed before every read: the stream ends when the camera stops sending,
        // without a token source per packet.
        var idle = _idle!;

        idle.CancelAfter(IdleTimeout);

        try
        {
            await _reader!.ReadExactlyAsync(buffer, idle.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("RTSP: the camera stopped sending video.");
        }
    }
}
