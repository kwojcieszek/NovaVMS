using System.Text;
using LibVLCSharp.Shared;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class RecordingExporterTests
{
    [Fact]
    public async Task ExportsBoundedPlayableMp4AndCleansCancelledOutput()
    {
        // Uses the same native runtime shipped with the application, not a mocked exporter.
        if (!OperatingSystem.IsWindows()) return;
        LibVLCSharp.Shared.Core.Initialize();
        using var vlc = new LibVLC("--no-osd", "--no-audio", "--no-video-title-show", "--vout=dummy");
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        vlc.Log += (_, e) => { if (e.Level >= LogLevel.Warning) logs.Enqueue(e.Message); };
        var folder = Path.Combine(Path.GetTempPath(), "NovaVMS-export-test-O'Brien-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source.y4m");
        try
        {
            await using (var file = File.Create(source))
            {
                await file.WriteAsync(Encoding.ASCII.GetBytes("YUV4MPEG2 W64 H64 F25:1 Ip A1:1 C420jpeg\n"));
                for (var frame = 0; frame < 150; frame++)
                {
                    await file.WriteAsync(Encoding.ASCII.GetBytes("FRAME\n"));
                    await file.WriteAsync(Enumerable.Repeat((byte)(30 + frame), 64 * 64)
                        .Concat(Enumerable.Repeat((byte)128, 64 * 64 / 2)).ToArray());
                }
            }
            var exporter = new RecordingExporter(vlc);
            var destination = Path.Combine(folder, "export.mp4");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await exporter.ExportAsync(VideoSource.FromUri(new Uri(source)), "", "", TimeSpan.FromSeconds(3), destination, null, timeout.Token);
            Assert.True(new FileInfo(destination).Length > 128);
            Assert.InRange(RecordingExporter.ReadMp4Duration(destination).TotalMilliseconds, 2000, 4500);
            using (var media = new Media(vlc, new Uri(destination)))
            using (var player = new MediaPlayer(vlc))
            {
                Assert.True(player.Play(media));
                for (var i = 0; i < 20 && media.Statistics.DecodedVideo == 0; i++) await Task.Delay(100);
                Assert.True(media.Statistics.DecodedVideo > 0, "Exported MP4 must contain decodable video.");
                await Task.Run(player.Stop);
            }
            Assert.DoesNotContain(Directory.GetFiles(folder), file => file.EndsWith(".partial"));

            var incomplete = Path.Combine(folder, "incomplete.mp4");
            await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(VideoSource.FromUri(new Uri(source)),
                "", "", TimeSpan.FromSeconds(10), incomplete, null, timeout.Token));
            Assert.False(File.Exists(incomplete));
            Assert.DoesNotContain(Directory.GetFiles(folder), file => file.EndsWith(".partial"));

            using var cancelled = new CancellationTokenSource();
            cancelled.CancelAfter(200);
            var cancelledFile = Path.Combine(folder, "cancelled.mp4");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(
                VideoSource.FromStream(() => new SlowStream(File.OpenRead(source))), "", "", TimeSpan.FromSeconds(6), cancelledFile, null, cancelled.Token));
            Assert.False(File.Exists(cancelledFile));
            Assert.DoesNotContain(Directory.GetFiles(folder), file => file.EndsWith(".partial"));
        }
        catch (Exception e)
        {
            throw new Exception(e.Message + "\n" + string.Join("\n", logs), e);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private sealed class SlowStream(Stream inner) : Stream
    {
        private readonly object _sync = new();
        private bool _closed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => -1;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(40);
            lock (_sync) return _closed ? 0 : inner.Read(buffer, offset, Math.Min(count, 4096));
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) lock (_sync) { _closed = true; inner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
