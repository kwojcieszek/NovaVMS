using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Feeds encrypted Hikvision packets to PlayCtrl. Its decoded YV12 video is exposed
/// as YUV4MPEG2, which LibVLC can display without knowing the proprietary encryption.
/// Audio callbacks are deliberately excluded from this video-only path.
/// </summary>
internal sealed class HikvisionStreamDecoder : IDisposable
{
    private static readonly ConcurrentDictionary<int, HikvisionStreamDecoder> Ports = new();
    private static readonly PlayCtrlDecodeCallback Callback = OnDecoded;
    private readonly IPlayCtrlApi _api;
    private readonly byte[] _key;
    private readonly bool _playback;
    private readonly Action<byte[]> _output;
    private readonly Action<string> _failed;
    private readonly Channel<(uint Type, byte[] Bytes)> _packets = Channel.CreateBounded<(uint, byte[])>(128);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private int _port = -1;
    private int _disposed;
    private bool _opened;
    private bool _playing;
    private int _width, _height;
    private int _hasFrames;

    public bool HasFrames => Volatile.Read(ref _hasFrames) != 0;

    public static byte[] EncodeKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains('\0') || Encoding.UTF8.GetByteCount(key) > 128)
            throw new NotSupportedException(ProtocolText.Get("StreamKeyInvalid"));
        // Do not trim, pad, hash, or log a user-supplied key. PlayCtrl interprets it.
        return Encoding.UTF8.GetBytes(key);
    }

    public HikvisionStreamDecoder(IPlayCtrlApi api, string key, bool playback, Action<byte[]> output, Action<string> failed)
    {
        _api = api;
        _key = EncodeKey(key);
        _playback = playback;
        _output = output;
        _failed = failed;
        _worker = Task.Run(RunAsync);
    }

    public void Feed(uint type, byte[] bytes)
    {
        if (Volatile.Read(ref _disposed) != 0 || _stop.IsCancellationRequested) return;
        if (!_packets.Writer.TryWrite((type, bytes))) Fail(ProtocolText.Get("StreamDecoderBufferFull"));
    }

    private async Task RunAsync()
    {
        try
        {
            if (!_api.GetPort(out var port)) throw new IOException(ProtocolText.Get("StreamDecoderPortFailed"));
            _port = port;
            Ports[_port] = this;
            await foreach (var packet in _packets.Reader.ReadAllAsync(_stop.Token))
            {
                if (packet.Type == HcNetSdkConstants.DataSystemHeader)
                {
                    if (_opened) throw new IOException(ProtocolText.Get("StreamDecoderChanged"));
                    Require(_api.SetMode(_port, _playback ? 1u : 0u));
                    Require(_api.OpenStream(_port, packet.Bytes, (uint)packet.Bytes.Length, 4 * 1024 * 1024));
                    _opened = true;
                    Require(_api.SetKey(_port, 1, _key, _key.Length));
                    Require(_api.SetCallback(_port, Callback, IntPtr.Zero, 0, IntPtr.Zero));
                    Require(_api.Play(_port, IntPtr.Zero));
                    _playing = true;
                }
                else if (packet.Type == HcNetSdkConstants.DataStream)
                {
                    if (!_opened) throw new IOException(ProtocolText.Get("StreamDecoderHeaderMissing"));
                    var wait = Stopwatch.StartNew();
                    while (!_api.Input(_port, packet.Bytes, (uint)packet.Bytes.Length))
                    {
                        if (_api.Error(_port) != 11 || wait.Elapsed > TimeSpan.FromSeconds(5))
                            Require(false);
                        await Task.Delay(5, _stop.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // No key or packet contents are included in error messages.
            Fail(exception is IOException ? exception.Message : ProtocolText.Get("StreamDecryptionFailed"));
        }
        finally
        {
            if (_port >= 0)
            {
                if (_playing) _api.Stop(_port);
                if (_opened) _api.Close(_port);
                Ports.TryRemove(_port, out _);
                _api.Free(_port);
            }
            CryptographicOperations.ZeroMemory(_key);
        }
    }

    private void Require(bool success)
    {
        if (!success) throw new IOException(ProtocolText.Format("StreamDecoderError", _api.Error(_port)));
    }

    private void Fail(string message)
    {
        if (!_stop.IsCancellationRequested)
        {
            _failed(message);
            _stop.Cancel();
            _packets.Writer.TryComplete();
        }
    }

    private static void OnDecoded(int port, IntPtr buffer, int size, ref PlayCtrlFrameInfo info, IntPtr user, int reserved)
    {
        if (!Ports.TryGetValue(port, out var decoder) || decoder._stop.IsCancellationRequested) return;
        try
        {
            // PlayCtrl frame type 3 is planar YV12; audio has types 100/101.
            if (info.Type != 3) return;
            if (decoder._width != 0 && (info.Width != decoder._width || info.Height != decoder._height))
                throw new IOException(ProtocolText.Get("StreamDecoderChanged"));
            var frame = ToYuv4Mpeg(buffer, size, info, !decoder.HasFrames);
            decoder._width = info.Width;
            decoder._height = info.Height;
            decoder._output(frame);
            Volatile.Write(ref decoder._hasFrames, 1);
        }
        catch (Exception)
        {
            // Exceptions must never cross a native callback boundary.
            decoder.Fail(ProtocolText.Get("StreamDecryptionFailed"));
        }
    }

    internal static byte[] ToYuv4Mpeg(IntPtr source, int size, PlayCtrlFrameInfo info, bool first)
    {
        if (source == IntPtr.Zero || info.Width is < 2 or > 8192 || info.Height is < 2 or > 8192
            || info.Width % 2 != 0 || info.Height % 2 != 0)
            throw new IOException("Invalid decoded video dimensions.");
        var luma = checked(info.Width * info.Height);
        var length = checked(luma + luma / 2);
        if (size < length || length > 32 * 1024 * 1024) throw new IOException("Invalid decoded video frame size.");
        var fps = info.FrameRate is > 0 and <= 120 ? info.FrameRate : 25;
        var header = Encoding.ASCII.GetBytes(first
            ? $"YUV4MPEG2 W{info.Width} H{info.Height} F{fps}:1 Ip A1:1 C420jpeg\nFRAME\n"
            : "FRAME\n");
        var bytes = new byte[header.Length + length];
        header.CopyTo(bytes, 0);
        Marshal.Copy(source, bytes, header.Length, luma);
        // YV12 is Y,V,U; YUV4MPEG2 C420 uses Y,U,V.
        Marshal.Copy(IntPtr.Add(source, luma + luma / 4), bytes, header.Length + luma, luma / 4);
        Marshal.Copy(IntPtr.Add(source, luma), bytes, header.Length + luma + luma / 4, luma / 4);
        return bytes;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _packets.Writer.TryComplete();
        // The owning pipe is completed before disposal, releasing blocked frame callbacks.
        _worker.GetAwaiter().GetResult();
        _stop.Dispose();
    }
}
