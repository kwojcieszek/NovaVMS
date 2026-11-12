using NovaVMS.Core.Localization;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.HcNetSdk;

namespace NovaVMS.Protocols.Rtsp;

/// <summary>
/// Hikvision encrypted RTSP video for LibVLC: the pictures are decrypted with the stream key of the device
/// (<see cref="HikvisionNalDecryptor"/>) and delivered as an MPEG transport stream. Streams other than
/// H.264/H.265 (MPEG program stream over RTP) are decoded by PlayCtrl instead.
/// </summary>
internal sealed class DecryptedRtspVideoStream : BlockingPipeStream, IVideoStreamStatus
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);

    private readonly Uri _uri;
    private readonly string _username;
    private readonly string _password;
    private readonly string _key;
    private readonly bool _playback;
    private readonly RtspVideoSession _session;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;

    private MpegTsVideoWriter? _writer;
    private HikvisionRtspVideoStream? _fallback;
    private string? _failure;
    private int _hasFrames;
    private int _disposed;

    public string? ErrorMessage => _failure ?? _fallback?.ErrorMessage;

    private DecryptedRtspVideoStream(Uri uri, string username, string password, string key, bool playback)
        : base(64 * 1024 * 1024, false, IdleTimeout)
    {
        _uri = uri;
        _username = username;
        _password = password;
        _key = key;
        _playback = playback;
        _session = new RtspVideoSession(uri, username, password, Write, key);
        _worker = Task.Run(RunAsync);
    }

    public static VideoSource CreateSource(Uri uri, Device device, bool playback)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(device);

        var clean = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;

        return VideoSource.FromEncryptedStream(clean,
            () => new DecryptedRtspVideoStream(clean, device.Username, device.Password, device.StreamEncryptionKey, playback));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Complete();
            _stop.Cancel();
            _fallback?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RunAsync()
    {
        try
        {
            await _session.RunAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (NotSupportedException) when (Volatile.Read(ref _hasFrames) == 0 && !_stop.IsCancellationRequested)
        {
            await PlayWithPlayCtrlAsync().ConfigureAwait(false);
        }
        catch (Exception) when (!_stop.IsCancellationRequested)
        {
            // Do not surface raw RTSP messages, credentials, URLs, or packet contents.
            _failure ??= LibraryText.Get("StreamRtspFailed");
        }
        catch (Exception)
        {
        }
        finally
        {
            Complete();
        }
    }

    private void Write(VideoAccessUnit unit)
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        if (_session.StreamKeyRejected)
        {
            _failure ??= LibraryText.Get("StreamKeyWrong");

            Complete();
            _stop.Cancel();

            return;
        }

        _writer ??= new MpegTsVideoWriter(unit.Codec);

        var packets = _writer.Write(unit);

        Append(packets);
        Volatile.Write(ref _hasFrames, 1);
    }

    private async Task PlayWithPlayCtrlAsync()
    {
        try
        {
            _fallback = HikvisionRtspVideoStream.Open(_uri, _username, _password, _key, _playback);
        }
        catch (NotSupportedException exception)
        {
            _failure ??= exception.Message;

            return;
        }

        if (_stop.IsCancellationRequested)
        {
            _fallback.Dispose();

            return;
        }

        var buffer = new byte[256 * 1024];

        while (!_stop.IsCancellationRequested)
        {
            var read = await Task.Run(() => _fallback.Read(buffer, 0, buffer.Length)).ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            Append(buffer.AsSpan(0, read).ToArray());
        }
    }
}
