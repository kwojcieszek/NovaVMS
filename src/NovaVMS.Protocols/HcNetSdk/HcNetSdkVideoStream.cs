using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Live or recorded video received through the SDK. The device sends MPEG program stream (or transport stream)
/// packets. PlayCtrl decodes video-only main streams and encrypted streams; other streams
/// are demultiplexed by the player, with the vendor's system header skipped.
/// The transfer starts in the background as soon as the stream is created and stops when it is disposed.
/// </summary>
internal sealed class HcNetSdkVideoStream : BlockingPipeStream, IVideoStreamStatus
{
    private const long LiveBufferBytes = 8 * 1024 * 1024;
    private const long PlaybackBufferBytes = 32 * 1024 * 1024;

    // NET_DVR_PREVIEWINFO is zeroed and only the used fields are written at their offsets.
    private const int PreviewInfoSize = 512;
    private const int MainStream = 0;
    private const int SubStream = 1;
    private const int TcpLink = 0;

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);
    private static readonly ConcurrentDictionary<long, HcNetSdkVideoStream> ActiveStreams = new();

    // Kept in a static field so the garbage collector never releases a delegate the SDK still calls.
    private static readonly HcNetSdkDataCallback DataCallback = OnData;

    private static long _nextId;

    private readonly long _id;
    private readonly bool _isPlayback;
    private NativeStreamLifetime? _lifetime;
    private readonly HikvisionStreamDecoder? _decoder;
    private readonly ProgramStreamTimestampAligner _timestamps = new();
    private string? _errorMessage;

    public string? ErrorMessage => _errorMessage ?? (_decoder is { HasFrames: false }
        ? ProtocolText.Get("StreamDecryptionNoFrames") : null);

    private HcNetSdkVideoStream(bool isPlayback, string encryptionKey, bool decodeVideo = false)
        : base(decodeVideo || !string.IsNullOrEmpty(encryptionKey) ? 64 * 1024 * 1024 : (isPlayback ? PlaybackBufferBytes : LiveBufferBytes),
            dropWhenFull: !isPlayback && !decodeVideo && string.IsNullOrEmpty(encryptionKey), IdleTimeout)
    {
        _id = Interlocked.Increment(ref _nextId);
        _isPlayback = isPlayback;
        if (decodeVideo || !string.IsNullOrEmpty(encryptionKey))
            _decoder = new HikvisionStreamDecoder(PlayCtrlApi.Get(), encryptionKey, isPlayback, Append, message =>
            {
                _errorMessage = message;
                Complete();
            });
    }

    public static Stream OpenLive(Device device, int channelNumber, StreamType streamType, bool decodeVideo = false)
    {
        var stream = new HcNetSdkVideoStream(isPlayback: false, device.StreamEncryptionKey, decodeVideo);
        stream.BeginStart(device, session => stream.StartLive(session, channelNumber, streamType));
        return stream;
    }

    public static Stream OpenPlayback(Device device, int channelNumber, DateTime start, DateTime end)
    {
        var stream = new HcNetSdkVideoStream(isPlayback: true, device.StreamEncryptionKey);
        stream.BeginStart(device, session => stream.StartPlayback(session, channelNumber, start, end));
        return stream;
    }

    /// <summary>
    /// Stops all transfers synchronously (before the SDK is released).
    /// </summary>
    public static void StopAll()
    {
        foreach (var stream in ActiveStreams.Values)
        {
            stream.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // VideoMedia.Stop runs off the UI thread. Do not return while SDK callbacks are still alive.
            _lifetime?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BeginStart(Device device, Func<HcNetSdkSession, int> start)
    {
        _lifetime = new NativeStreamLifetime(token => StartAsync(device, start, token), StopHandle, Complete, () =>
        {
            try { _decoder?.Dispose(); }
            finally { ActiveStreams.TryRemove(_id, out _); }
        }, startImmediately: false);
        ActiveStreams[_id] = this;
        _lifetime.Start();
    }

    private async Task<int> StartAsync(Device device, Func<HcNetSdkSession, int> start, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var connection = await HcNetSdkSessionPool.TryAcquireAsync(device, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (connection.Session is not { } session)
            {
                _errorMessage = connection.Error!.Message;
                Complete();
                return -1;
            }

            var handle = start(session);
            if (handle < 0)
            {
                var code = HcNetSdkNative.GetLastError();
                _errorMessage = HcNetSdkErrors.FromCode(code, device.Host, device.SdkPort).Message;
                if (HcNetSdkErrors.InvalidatesSession(code))
                {
                    HcNetSdkSessionPool.Invalidate(session);
                }

                // Ending the stream makes the player report the failure; the tile retries later.
                Complete();
                return -1;
            }
            return handle;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return -1; }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            _errorMessage = exception.Message;
            Complete();
            return -1;
        }
    }

    private int StartLive(HcNetSdkSession session, int channelNumber, StreamType streamType)
    {
        var previewInfo = Marshal.AllocHGlobal(PreviewInfoSize);
        try
        {
            Marshal.Copy(new byte[PreviewInfoSize], 0, previewInfo, PreviewInfoSize);

            // lChannel, dwStreamType, dwLinkMode, hPlayWnd (none - data goes to the callback), bBlocked.
            Marshal.WriteInt32(previewInfo, 0, session.ToSdkChannel(channelNumber));
            Marshal.WriteInt32(previewInfo, 4, streamType == StreamType.Main ? MainStream : SubStream);
            Marshal.WriteInt32(previewInfo, 8, TcpLink);
            var windowOffset = IntPtr.Size == 8 ? 16 : 12;

            // Blocking start: an unavailable channel fails here instead of producing an empty stream.
            Marshal.WriteInt32(previewInfo, windowOffset + IntPtr.Size, 1);

            return HcNetSdkNative.RealPlay(session.UserId, previewInfo, DataCallback, new IntPtr(_id));
        }
        finally
        {
            Marshal.FreeHGlobal(previewInfo);
        }
    }

    private int StartPlayback(HcNetSdkSession session, int channelNumber, DateTime start, DateTime end)
    {
        var from = NetDvrTime.From(start);
        var to = NetDvrTime.From(end);
        var handle = HcNetSdkNative.PlayBackByTime(session.UserId, session.ToSdkChannel(channelNumber), ref from, ref to, IntPtr.Zero);
        if (handle < 0)
        {
            return handle;
        }

        uint outLength = 0;
        if (!HcNetSdkNative.SetPlayDataCallback(handle, DataCallback, new IntPtr(_id))
            || !HcNetSdkNative.PlayBackControl(handle, HcNetSdkConstants.PlaybackStart, IntPtr.Zero, 0, IntPtr.Zero, ref outLength))
        {
            // StopPlayBack may overwrite the thread-local SDK error.
            var failure = HcNetSdkErrors.LastError(session.Host, session.Port);
            HcNetSdkNative.StopPlayBack(handle);
            throw failure;
        }

        return handle;
    }

    private void StopHandle(int handle)
    {
        if (_isPlayback)
        {
            HcNetSdkNative.StopPlayBack(handle);
        }
        else
        {
            HcNetSdkNative.StopRealPlay(handle);
        }
    }

    private static void OnData(int handle, uint dataType, IntPtr buffer, uint bufferSize, IntPtr user)
    {
        if (dataType is not (HcNetSdkConstants.DataStream or HcNetSdkConstants.DataSystemHeader)
            || buffer == IntPtr.Zero || bufferSize == 0)
        {
            return;
        }

        if (!ActiveStreams.TryGetValue(user.ToInt64(), out var stream))
        {
            return;
        }

        var chunk = new byte[bufferSize];
        Marshal.Copy(buffer, chunk, 0, chunk.Length);
        if (stream._decoder is not null) stream._decoder.Feed(dataType, chunk);
        else if (dataType == HcNetSdkConstants.DataStream)
        {
            // Callbacks of one handle arrive in order, so the aligner keeps its clock state per stream.
            stream.Append(stream._timestamps.Process(chunk));
        }
    }
}
