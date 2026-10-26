using System.Runtime.InteropServices;
using System.Text;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.HcNetSdk;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class HikvisionStreamDecoderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("ABCDEF")]
    [InlineData("stream-secret")]
    [InlineData("klucz-ąę")]
    public async Task DecodesWithOptionalKeyAndReleasesNativePort(string key)
    {
        var api = new FakePlayCtrl();
        var output = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var decoder = new HikvisionStreamDecoder(api, key, false,
                   bytes => output.TrySetResult(bytes), message => failure.TrySetResult(message)))
        {
            decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 3]);
            decoder.Feed(HcNetSdkConstants.DataStream, [4, 5, 6]);
            var completed = await Task.WhenAny(output.Task, failure.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(output.Task, completed);
            var frame = await output.Task;
            Assert.StartsWith("YUV4MPEG2 W2 H2 F25:1 Ip A1:1 C420jpeg\nFRAME\n", Encoding.ASCII.GetString(frame));
            // PlayCtrl supplies Y,V,U; the player must receive Y,U,V.
            Assert.Equal(new byte[] { 1, 2, 3, 4, 6, 5 }, frame[^6..]);
            Assert.Equal(Encoding.UTF8.GetBytes(key), api.Key);
            Assert.Equal(Encoding.UTF8.GetByteCount(key) * 8, api.KeyLengthBits);
            Assert.Equal(key.Length == 0 ? 0 : 1, api.KeyCalls);
            Assert.Equal(2, api.InputCalls); // A full native input buffer must be retried, not dropped.
        }
        Assert.True(api.Stopped);
        Assert.True(api.Closed);
        Assert.True(api.Freed);
    }

    [Fact]
    public void ReopeningSourcePassesCurrentAudioSelection()
    {
        var selections = new List<bool>();
        var source = VideoSource.FromStream(withAudio =>
        {
            selections.Add(withAudio);
            return new MemoryStream();
        });
        using var muted = source.OpenStream(false);
        using var audible = source.OpenStream(true);
        using var reconnected = source.OpenStream(false);
        Assert.Equal(new[] { false, true, false }, selections);
    }

    [Fact]
    public async Task MainKeyFrameBurstCanQueueMoreThan128FragmentsDuringDecoderStartup()
    {
        using var gate = new ManualResetEventSlim();
        var api = new FakePlayCtrl { OpenGate = gate };
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var decoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var decoder = new HikvisionStreamDecoder(api, "key", false,
            _ => decoded.TrySetResult(), message => failed.TrySetResult(message));
        try
        {
            decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 3]);
            await api.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var index = 0; index < 1024; index++)
                decoder.Feed(HcNetSdkConstants.DataStream, new byte[1024]);
            Assert.False(failed.Task.IsCompleted);
            gate.Set();
            await decoded.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(failed.Task.IsCompleted);
        }
        finally { gate.Set(); }
    }

    [Fact]
    public async Task QueueStillRejectsExcessiveEncodedDataWithoutBlockingSdkCallback()
    {
        using var gate = new ManualResetEventSlim();
        var api = new FakePlayCtrl { OpenGate = gate };
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var decoder = new HikvisionStreamDecoder(api, "key", false, _ => { }, message => failed.TrySetResult(message));
        try
        {
            decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 3]);
            await api.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var packet = new byte[1024 * 1024];
            for (var index = 0; index <= HikvisionStreamDecoder.MaximumQueuedPacketBytes / packet.Length; index++)
                decoder.Feed(HcNetSdkConstants.DataStream, packet);
            Assert.False(string.IsNullOrEmpty(await failed.Task.WaitAsync(TimeSpan.FromSeconds(3))));
        }
        finally { gate.Set(); }
    }

    [Fact]
    public async Task RecordingQueueHoldsSdkCallbackUntilDecoderCatchesUpOrStops()
    {
        using var gate = new ManualResetEventSlim();
        var api = new FakePlayCtrl { OpenGate = gate };
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var decoder = new HikvisionStreamDecoder(api, "key", true, _ => { }, message => failed.TrySetResult(message));
        try
        {
            decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 3]);
            await api.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var packet = new byte[1024 * 1024];
            var feeding = Task.Run(() =>
            {
                for (var index = 0; index <= HikvisionStreamDecoder.MaximumQueuedPacketBytes / packet.Length; index++)
                    decoder.Feed(HcNetSdkConstants.DataStream, packet);
            });
            await Task.Delay(300);
            Assert.False(feeding.IsCompleted);
            Assert.False(failed.Task.IsCompleted);
            decoder.Stop();
            await feeding.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(failed.Task.IsCompleted);
        }
        finally { gate.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedIdenticalHeaderKeepsLiveAndRecordingDecoderRunning(bool playback)
    {
        var api = new FakePlayCtrl();
        var frames = 0;
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var decoder = new HikvisionStreamDecoder(api, "key", playback,
            _ => { if (Interlocked.Increment(ref frames) == 2) complete.TrySetResult(); }, message => failed.TrySetResult(message));
        decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 3]);
        decoder.Feed(HcNetSdkConstants.DataStream, [4, 5, 6]);
        decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 3]);
        decoder.Feed(HcNetSdkConstants.DataStream, [7, 8, 9]);
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(failed.Task.IsCompleted);
        Assert.Equal(1, api.OpenCalls);
        Assert.Equal(1, api.KeyCalls);
    }

    [Fact]
    public async Task ActuallyChangedHeaderStillRequestsReconnect()
    {
        var api = new FakePlayCtrl();
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var decoder = new HikvisionStreamDecoder(api, "", true, _ => { }, message => failed.TrySetResult(message));
        decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 3]);
        decoder.Feed(HcNetSdkConstants.DataSystemHeader, [1, 2, 4]);
        Assert.False(string.IsNullOrWhiteSpace(await failed.Task.WaitAsync(TimeSpan.FromSeconds(3))));
        Assert.Equal(1, api.OpenCalls);
    }

    private sealed class FakePlayCtrl : IPlayCtrlApi
    {
        private static int _nextPort;
        private PlayCtrlDecodeCallback? _callback;
        public ManualResetEventSlim? OpenGate { get; init; }
        public TaskCompletionSource OpenEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] Key { get; private set; } = [];
        public int KeyCalls { get; private set; }
        public int KeyLengthBits { get; private set; }
        public int InputCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public bool Stopped { get; private set; }
        public bool Closed { get; private set; }
        public bool Freed { get; private set; }
        public bool GetPort(out int port) { port = Interlocked.Increment(ref _nextPort); return true; }
        public bool SetMode(int port, uint mode) => mode is 0 or 1;
        public bool OpenStream(int port, byte[] header, uint size, uint capacity)
        {
            OpenCalls++;
            OpenEntered.TrySetResult();
            if (OpenGate is not null && !OpenGate.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return true;
        }
        public bool SetKey(int port, int type, byte[] key, int lengthBits)
        {
            KeyCalls++;
            KeyLengthBits = lengthBits;
            Key = key.ToArray();
            return type == 1 && lengthBits == key.Length * 8;
        }
        public bool SetCallback(int port, PlayCtrlDecodeCallback callback, IntPtr destination, int size, IntPtr user)
        {
            _callback = callback;
            return true;
        }
        public bool Play(int port, IntPtr window) => true;
        public bool Input(int port, byte[] bytes, uint size)
        {
            if (++InputCalls == 1) return false;
            var buffer = Marshal.AllocHGlobal(6);
            try
            {
                Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6 }, 0, buffer, 6);
                var info = new PlayCtrlFrameInfo { Width = 2, Height = 2, Type = 3, FrameRate = 25 };
                _callback!(port, buffer, 6, ref info, IntPtr.Zero, 0);
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return true;
        }
        public bool Stop(int port) { Stopped = true; return true; }
        public bool Close(int port) { Closed = true; return true; }
        public bool Free(int port) { Freed = true; return true; }
        public uint Error(int port) => 11;
    }
}
